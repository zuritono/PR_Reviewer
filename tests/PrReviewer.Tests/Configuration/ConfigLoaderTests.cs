using PrReviewer.Configuration;
using PrReviewer.Domain;

namespace PrReviewer.Tests.Configuration;

/// <summary>
/// Each test gets its own empty temporary folder and a fake environment,
/// so the real config.json and real environment variables never leak in.
/// </summary>
public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("prreviewer-tests-").FullName;
    private readonly Dictionary<string, string> _environment = [];
    private readonly StringWriter _log = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteConfig(string json) => File.WriteAllText(Path.Combine(_folder, "config.json"), json);

    private AppConfig Load() => ConfigLoader.Load(_log, _folder, name => _environment.GetValueOrDefault(name));

    [Fact]
    public void Missing_file_means_defaults_with_dry_run_on()
    {
        var config = Load();

        Assert.True(config.DryRun);
        Assert.Equal(new AppConfig(), config);
        Assert.Contains("config.json not found", _log.ToString());
    }

    [Fact]
    public void Missing_dry_run_entry_still_means_dry_run()
    {
        WriteConfig("""{ "provider": "gemini", "gemini_api_key": "real-key" }""");

        Assert.True(Load().DryRun);
    }

    [Fact]
    public void Reads_settings_from_the_file()
    {
        WriteConfig("""
            {
              "dry_run": false,
              "model": "gemini-3.5-flash",
              "min_severity": "Warning",
              "max_findings": 3,
              "gemini_api_key": "file-key"
            }
            """);

        var config = Load();

        Assert.False(config.DryRun);
        Assert.Equal("gemini-3.5-flash", config.Model);
        Assert.Equal(Severity.Warning, config.MinSeverity);
        Assert.Equal(3, config.MaxFindings);
        Assert.Equal("file-key", config.GeminiApiKey);
    }

    [Fact]
    public void Allows_comments_and_trailing_commas()
    {
        WriteConfig("""
            {
              "model": "gemini-3.5-flash", // switched from lite
              "dry_run": false,
            }
            """);

        Assert.Equal("gemini-3.5-flash", Load().Model);
    }

    [Fact]
    public void Environment_variables_win_over_the_file()
    {
        WriteConfig("""{ "anthropic_api_key": "file-ant", "gemini_api_key": "file-key", "github_token": "file-token" }""");
        _environment["ANTHROPIC_API_KEY"] = "env-ant";
        _environment["GEMINI_API_KEY"] = "env-key";
        _environment["GITHUB_TOKEN"] = "env-token";

        var config = Load();

        Assert.Equal("env-ant", config.AnthropicApiKey);
        Assert.Equal("env-key", config.GeminiApiKey);
        Assert.Equal("env-token", config.GitHubToken);
    }

    [Fact]
    public void Template_placeholders_count_as_no_key()
    {
        WriteConfig("""
            {
              "anthropic_api_key": "sk-ant-YOUR-KEY-HERE",
              "gemini_api_key": "YOUR-GEMINI-KEY-HERE",
              "github_token": "YOUR-GITHUB-TOKEN-HERE"
            }
            """);

        var config = Load();

        Assert.Null(config.AnthropicApiKey);
        Assert.Null(config.GeminiApiKey);
        Assert.Null(config.GitHubToken);
    }

    [Theory]
    [InlineData("""{ "max_findings": 0 }""", "max_findings must be at least 1")]
    [InlineData("""{ "max_total_content_chars": 0 }""", "max_total_content_chars must be at least 1")]
    [InlineData("""{ "min_severity": "Huge" }""", "config.json is not valid")]
    [InlineData("""{ "dry_run": """, "config.json is not valid")]
    public void Invalid_settings_are_reported_clearly(string json, string message)
    {
        WriteConfig(json);

        var error = Assert.Throws<ConfigException>(Load);

        Assert.StartsWith(message, error.Message);
    }
}
