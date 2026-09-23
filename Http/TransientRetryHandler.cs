using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace PrReviewer.Http;

/// <summary>
/// Retries requests that failed with a temporary server-side error: 429
/// (rate limited), 500, 502, 503 (overloaded, common on Gemini's free
/// tier) and 504. When the server says how long to wait, that wait is
/// used; otherwise each retry waits a little longer. Any other response,
/// including the last failed attempt, is returned unchanged so the
/// caller's normal error handling applies.
/// </summary>
public partial class TransientRetryHandler(TextWriter log, IReadOnlyList<TimeSpan>? delays = null) : DelegatingHandler
{
    private static readonly TimeSpan[] DefaultDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    /// <summary>
    /// A server-requested wait longer than this (e.g. a daily quota
    /// running out) won't pass within one run, so give up straight away
    /// instead of spending more requests against the same limit.
    /// </summary>
    private static readonly TimeSpan MaxServerWait = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Google APIs (Gemini included) put the wait in the error body rather
    /// than a Retry-After header:
    /// "details": [{ "@type": "...RetryInfo", "retryDelay": "31s" }]
    /// </summary>
    [GeneratedRegex(@"""retryDelay""\s*:\s*""(\d+(?:\.\d+)?)s""")]
    private static partial Regex GoogleRetryDelay();

    private readonly IReadOnlyList<TimeSpan> _delays = delays ?? DefaultDelays;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var retry = 0; retry < _delays.Count; retry++)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (!IsTransient(response.StatusCode))
            {
                return response;
            }

            var serverWait = await ServerRequestedWait(response, cancellationToken);
            if (serverWait > MaxServerWait)
            {
                return response;
            }

            var delay = serverWait ?? _delays[retry];
            var reason = serverWait is null ? "" : " as the server asked";
            log.WriteLine(
                $"{request.RequestUri?.Host} returned {(int)response.StatusCode} {response.ReasonPhrase}; "
                + $"retrying in {Math.Ceiling(delay.TotalSeconds):0}s{reason} ({retry + 1} of {_delays.Count})...");
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        // Out of retries: one last attempt, returned whatever it is.
        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests
        or HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// The standard Retry-After header if present, else Google's
    /// retryDelay from the error body, else null.
    /// </summary>
    private static async Task<TimeSpan?> ServerRequestedWait(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        switch (response.Headers.RetryAfter)
        {
            case { Delta: { } delta }:
                return delta;
            case { Date: { } date }:
                // A date already in the past means "now".
                return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        }

        // Buffered, so the caller can still read the body if this response
        // ends up being returned.
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = GoogleRetryDelay().Match(body);
        return match.Success
            ? TimeSpan.FromSeconds(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            : null;
    }
}
