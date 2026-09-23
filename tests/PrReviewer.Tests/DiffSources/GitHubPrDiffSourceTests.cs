using System.Net;
using PrReviewer.DiffSources;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests.DiffSources;

public class GitHubPrDiffSourceTests
{
    private static readonly GitHubPrUrl Pr = new("octocat", "Hello-World", 1);

    private static (GitHubPrDiffSource Source, FakeHttpHandler Server) Create(string? token, params Func<HttpResponseMessage>[] responses)
    {
        var server = new FakeHttpHandler(responses);
        return (new GitHubPrDiffSource(new HttpClient(server), Pr, token, TextWriter.Null), server);
    }

    [Fact]
    public async Task Asks_the_pulls_api_for_the_diff_media_type()
    {
        var (source, server) = Create(null, () => FakeHttpHandler.Text(HttpStatusCode.OK, "diff --git a/x b/x"));

        var diff = await source.GetDiffAsync();

        Assert.Equal("diff --git a/x b/x", diff);
        var request = Assert.Single(server.Requests);
        Assert.Equal("https://api.github.com/repos/octocat/Hello-World/pulls/1", request.RequestUri!.ToString());
        Assert.Equal("application/vnd.github.diff", request.Headers.Accept.Single().MediaType);
        Assert.NotEmpty(request.Headers.UserAgent);
    }

    [Fact]
    public async Task Sends_no_authorization_without_a_token()
    {
        var (source, server) = Create(null, () => FakeHttpHandler.Text(HttpStatusCode.OK, "d"));

        await source.GetDiffAsync();

        Assert.Null(server.Requests.Single().Headers.Authorization);
    }

    [Fact]
    public async Task Sends_the_token_as_bearer_when_set()
    {
        var (source, server) = Create("ghp_test", () => FakeHttpHandler.Text(HttpStatusCode.OK, "d"));

        await source.GetDiffAsync();

        var auth = server.Requests.Single().Headers.Authorization!;
        Assert.Equal(("Bearer", "ghp_test"), (auth.Scheme, auth.Parameter));
    }

    [Fact]
    public async Task Not_found_without_token_hints_at_private_repos()
    {
        var (source, _) = Create(null, () => FakeHttpHandler.Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        var error = await Assert.ThrowsAsync<DiffSourceException>(source.GetDiffAsync);

        Assert.Contains("not found", error.Message);
        Assert.Contains("github_token", error.Message);
    }

    [Fact]
    public async Task Rate_limit_is_explained()
    {
        var (source, _) = Create(null, () =>
        {
            var response = FakeHttpHandler.Json(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}""");
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });

        var error = await Assert.ThrowsAsync<DiffSourceException>(source.GetDiffAsync);

        Assert.StartsWith("GitHub rate limit reached.", error.Message);
    }

    [Fact]
    public async Task Bad_token_is_explained()
    {
        var (source, _) = Create("wrong", () => FakeHttpHandler.Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}"""));

        var error = await Assert.ThrowsAsync<DiffSourceException>(source.GetDiffAsync);

        Assert.Contains("rejected the token", error.Message);
    }
}
