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

    public static AppConfig Load(TextWriter log)
    {
        // Next to the executable, not the current directory, so the tool
        // can be run from inside any repo. The build copies config.json
        // there (see PR_Reviewer.csproj).
        var path = Path.Combine(AppContext.BaseDirectory, FileName);

        AppConfig config;
        if (!File.Exists(path))
        {
            log.WriteLine($"{FileName} not found in {AppContext.BaseDirectory}, using defaults (dry_run = true).");
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

        // Environment variables take precedence over keys in the file.
        var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(geminiKey))
        {
            config = config with { GeminiApiKey = geminiKey };
        }

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
}

public class ConfigException(string message) : Exception(message);
