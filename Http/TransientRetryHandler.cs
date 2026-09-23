using System.Net;

namespace PrReviewer.Http;

/// <summary>
/// Retries requests that failed with a temporary server-side error: 429
/// (rate limited), 500, 502, 503 (overloaded, common on Gemini's free
/// tier) and 504. Waits a little longer before each retry, or as long as
/// the server's Retry-After header asks if that's short enough. Any other
/// response, including the last failed attempt, is returned unchanged so
/// the caller's normal error handling applies.
/// </summary>
public class TransientRetryHandler(TextWriter log, IReadOnlyList<TimeSpan>? delays = null) : DelegatingHandler
{
    private static readonly TimeSpan[] DefaultDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    /// <summary>
    /// A Retry-After longer than this (e.g. a daily quota running out)
    /// means waiting won't help within one run, so give up instead.
    /// </summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<TimeSpan> _delays = delays ?? DefaultDelays;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (!IsTransient(response.StatusCode) || retry == _delays.Count)
            {
                return response;
            }

            var delay = RetryAfter(response) ?? _delays[retry];
            if (delay > MaxRetryAfter)
            {
                return response;
            }

            log.WriteLine(
                $"{request.RequestUri?.Host} returned {(int)response.StatusCode} {response.ReasonPhrase}; "
                + $"retrying in {delay.TotalSeconds:0}s ({retry + 1} of {_delays.Count})...");
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests
        or HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.GatewayTimeout;

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            // A date already in the past means "now".
            { Date: { } date } => date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero,
            _ => null
        };
}
