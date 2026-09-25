using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrReviewer.Configuration;

/// <summary>
/// Loads config.json from the app's own folder. A missing file is not an
/// error: every setting falls back to its default, including
/// dry_run = true.
/// </summary>
public static class ConfigLoader
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <param name="log">Where to write the "not found" note.</param>
    /// <param name="directory">Folder holding config.json. Defaults to the
    /// app's own folder, not the current directory, so the tool can be run
    /// from inside any repo; the build copies config.json there (see
    /// PR_Reviewer.csproj). Tests pass a temporary folder.</param>
    /// <param name="environment">Reads an environment variable. Defaults to
    /// the real environment; tests pass a fake one.</param>
    public static AppConfig Load(
        TextWriter log,
        string? directory = null,
        Func<string, string?>? environment = null)
    {
        directory ??= AppContext.BaseDirectory;
        environment ??= Environment.GetEnvironmentVariable;
        var path = Path.Combine(directory, FileName);

        AppConfig config;
        if (!File.Exists(path))
        {
            log.WriteLine($"{FileName} not found in {directory}, using defaults (dry_run = true).");
            config = new AppConfig();
        }
        else
        {
            try
            {
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions)
                         ?? new AppConfig();
            }
            catch (JsonException ex)
            {
                throw new ConfigException($"{FileName} is not valid: {ex.Message}");
            }
        }

        // Environment variables take precedence over keys in the file, and
        // the masked placeholders from config.json.template count as no key.
        config = config with
        {
            AnthropicApiKey = RealKeyOrNull(environment("ANTHROPIC_API_KEY"))
                              ?? RealKeyOrNull(config.AnthropicApiKey),
            GeminiApiKey = RealKeyOrNull(environment("GEMINI_API_KEY"))
                           ?? RealKeyOrNull(config.GeminiApiKey),
            GitHubToken = RealKeyOrNull(environment("GITHUB_TOKEN"))
                          ?? RealKeyOrNull(config.GitHubToken)
        };

        if (config.MaxFindings < 1)
        {
            throw new ConfigException($"max_findings must be at least 1, got {config.MaxFindings}.");
        }

        if (config.MaxTotalContentChars < 1)
        {
            throw new ConfigException(
                $"max_total_content_chars must be at least 1, got {config.MaxTotalContentChars}.");
        }

        return config;
    }

    /// <summary>
    /// Null for an empty value or a template placeholder ("YOUR-..."),
    /// including values like sk-ant-YOUR-KEY-HERE.
    /// </summary>
    private static string? RealKeyOrNull(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Contains("YOUR-", StringComparison.OrdinalIgnoreCase)
            ? null
            : key.Trim();
}

public class ConfigException(string message) : Exception(message);
