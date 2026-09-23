using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PrReviewer.DiffSources;

/// <summary>
/// Fetches a pull request's diff from the GitHub REST API. Asking for the
/// "diff" media type returns the whole PR as one unified diff, the same
/// format as <c>git diff</c>. Works without a token for public repos
/// (60 requests an hour); a token is needed for private repos and raises
/// the limit.
/// </summary>
public class GitHubPrDiffSource(HttpClient http, GitHubPrUrl pr, string? token, TextWriter log) : IDiffSource
{
    private const string ApiBase = "https://api.github.com";

    public async Task<string> GetDiffAsync()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{ApiBase}/repos/{pr.Owner}/{pr.Repo}/pulls/{pr.Number}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.diff"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        // GitHub rejects API requests without a User-Agent.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PrReviewer", "1.0"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        log.WriteLine($"Fetching {pr} from GitHub...");
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        throw new DiffSourceException(ErrorFor(response, body));
    }

    private string ErrorFor(HttpResponseMessage response, string body)
    {
        var apiMessage = ApiMessage(body);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return token is null
                ? $"{pr} not found. Check the link; if the repo is private, set github_token in config.json or GITHUB_TOKEN."
                : $"{pr} not found, or your GitHub token can't read that repo.";
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
            && remaining.FirstOrDefault() == "0")
        {
            var hint = token is null
                ? " Without a token GitHub allows 60 requests an hour; set github_token or GITHUB_TOKEN for more."
                : "";
            return $"GitHub rate limit reached.{hint}";
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return "GitHub rejected the token (401). Check github_token / GITHUB_TOKEN, or remove it for public repos.";
        }

        // 406/422: GitHub won't produce a diff this large through the API.
        return $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase} for {pr}: {apiMessage}";
    }

    private static string ApiMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON; show it as-is below.
        }

        return body;
    }
}
