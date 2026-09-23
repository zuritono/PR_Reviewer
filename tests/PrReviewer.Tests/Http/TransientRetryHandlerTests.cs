using System.Net;
using System.Net.Http.Headers;
using PrReviewer.Http;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests.Http;

public class TransientRetryHandlerTests
{
    // Short waits so the tests run fast; the real handler waits 2, 5, 10 s.
    private static readonly TimeSpan[] ShortDelays =
        [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)];

    private static HttpResponseMessage Status(HttpStatusCode status) => FakeHttpHandler.Text(status, status.ToString());

    /// <summary>A Gemini-style quota error: the wait is in the body, not a header.</summary>
    private static HttpResponseMessage GoogleQuota(string retryDelay) => FakeHttpHandler.Json(HttpStatusCode.TooManyRequests,
        $$$"""{"error":{"code":429,"details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"{{{retryDelay}}}"}]}}""");

    private static async Task<(HttpResponseMessage Response, FakeHttpHandler Server, string Log)> Send(
        params Func<HttpResponseMessage>[] responses)
    {
        var server = new FakeHttpHandler(responses);
        var log = new StringWriter();
        var client = new HttpClient(new TransientRetryHandler(log, ShortDelays) { InnerHandler = server });
        var response = await client.PostAsync("https://api.test/x", new StringContent("{\"p\":1}"));
        return (response, server, log.ToString());
    }

    [Fact]
    public async Task Success_is_not_retried()
    {
        var (response, server, _) = await Send(() => Status(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, server.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Client_errors_are_not_retried(HttpStatusCode status)
    {
        var (response, server, _) = await Send(() => Status(status));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, server.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Temporary_errors_are_retried_until_success(HttpStatusCode status)
    {
        var (response, server, _) = await Send(() => Status(status), () => Status(status), () => Status(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, server.Calls);
    }

    [Fact]
    public async Task Gives_up_after_three_retries_and_returns_the_last_response()
    {
        var (response, server, log) = await Send(() => Status(HttpStatusCode.ServiceUnavailable));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(4, server.Calls);
        Assert.Contains("(3 of 3)", log);
        Assert.Equal("ServiceUnavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Succeeds_on_the_very_last_attempt()
    {
        var (response, server, _) = await Send(
            () => Status(HttpStatusCode.ServiceUnavailable), () => Status(HttpStatusCode.ServiceUnavailable),
            () => Status(HttpStatusCode.ServiceUnavailable), () => Status(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4, server.Calls);
    }

    [Fact]
    public async Task Resends_the_same_request_body_each_time()
    {
        var (_, server, _) = await Send(() => Status(HttpStatusCode.ServiceUnavailable), () => Status(HttpStatusCode.OK));

        Assert.Equal(["{\"p\":1}", "{\"p\":1}"], server.Bodies);
    }

    [Fact]
    public async Task Waits_as_long_as_googles_retry_delay_asks()
    {
        var (response, server, log) = await Send(() => GoogleQuota("0.2s"), () => Status(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Calls);
        Assert.Contains("as the server asked", log);
    }

    [Fact]
    public async Task Stops_at_once_when_the_server_asks_for_a_long_wait()
    {
        var (response, server, log) = await Send(() => GoogleQuota("120s"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, server.Calls);
        Assert.Empty(log);
        Assert.Contains("retryDelay", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Retry_after_header_wins_over_the_body()
    {
        var (response, server, log) = await Send(() =>
        {
            var quota = GoogleQuota("120s");
            quota.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return quota;
        }, () => Status(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Calls);
        Assert.Contains("as the server asked", log);
    }
}
