using System.Text.Json.Serialization;
using PrReviewer.Domain;

namespace PrReviewer.Configuration;

/// <summary>
/// Settings read from config.json. Only the settings the current code
/// uses are here; keys for other providers are added along with those
/// providers. Defaults apply to anything missing from the file, and
/// DryRun defaults to true so a missing or incomplete config can never
/// lead to a billed API call.
/// </summary>
public record AppConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "gemini";

    [JsonPropertyName("model")]
    public string Model { get; init; } = "gemini-3.5-flash-lite";

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; init; } = true;

    [JsonPropertyName("gemini_api_key")]
    public string? GeminiApiKey { get; init; }

    /// <summary>
    /// Optional. Only needed to review PRs in private repos, or to go past
    /// GitHub's 60 requests an hour without one.
    /// </summary>
    [JsonPropertyName("github_token")]
    public string? GitHubToken { get; init; }

    [JsonPropertyName("min_severity")]
    public Severity MinSeverity { get; init; } = Severity.Suggestion;

    [JsonPropertyName("max_findings")]
    public int MaxFindings { get; init; } = 10;

    /// <summary>
    /// Diffs longer than this are refused rather than sent, as a basic
    /// guard against an accidentally huge (and costly) request.
    /// </summary>
    [JsonPropertyName("max_total_content_chars")]
    public int MaxTotalContentChars { get; init; } = 150_000;
}
