using System.Net;
using System.Text;

namespace PrReviewer.Tests.Helpers;

/// <summary>
/// Stands in for a web server: returns the given responses in order (the
/// last one repeats) and records every request it receives, including
/// its body, so tests can check what would have been sent.
/// </summary>
public sealed class FakeHttpHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public int Calls => Requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return responses[Math.Min(Calls - 1, responses.Length - 1)]();
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
}
