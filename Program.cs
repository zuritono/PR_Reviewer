using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PrReviewer.Configuration;
using PrReviewer.DiffSources;
using PrReviewer.Output;
using PrReviewer.Prompts;
using PrReviewer.Reviewers;

namespace PrReviewer;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // The printer uses "—"; the default Windows console code page
        // would show it as "?".
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: PrReviewer <diff-file>");
            return 1;
        }

        try
        {
            var config = ConfigLoader.Load(Console.Error);
            using var services = BuildServices(config, args[0]);
            return await services.GetRequiredService<ReviewRunner>().RunAsync();
        }
        catch (Exception ex) when (ex is ConfigException or FileNotFoundException or ProviderException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Couldn't reach the provider's API: {ex.Message}");
            return 1;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("The provider's API didn't answer in time. Try again, or review a smaller diff.");
            return 1;
        }
    }

    /// <summary>
    /// Composition root: the only place that knows which implementations
    /// are in use. Everything downstream receives interfaces.
    /// </summary>
    private static ServiceProvider BuildServices(AppConfig config, string diffPath)
    {
        var services = new ServiceCollection();

        services.AddSingleton(config);
        services.AddSingleton<IDiffSource>(new LocalFileDiffSource(diffPath));
        AddReviewer(services, config);
        services.AddSingleton<ReviewFilter>();
        services.AddSingleton(new ConsoleReviewPrinter(Console.Out));
        services.AddSingleton(sp => new ReviewRunner(
            sp.GetRequiredService<IDiffSource>(),
            sp.GetRequiredService<ICodeReviewer>(),
            sp.GetRequiredService<ReviewFilter>(),
            sp.GetRequiredService<ConsoleReviewPrinter>(),
            config,
            Console.Error));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// dry_run wins over provider, so a real (billed) reviewer is only
    /// ever registered when dry_run is explicitly false.
    /// </summary>
    private static void AddReviewer(IServiceCollection services, AppConfig config)
    {
        if (config.DryRun)
        {
            services.AddSingleton<ICodeReviewer, DryRunCodeReviewer>();
            return;
        }

        switch (config.Provider.ToLowerInvariant())
        {
            case "gemini":
                if (IsMissingKey(config.GeminiApiKey))
                {
                    throw new ConfigException(
                        $"No Gemini API key. Set GEMINI_API_KEY or gemini_api_key in {ConfigLoader.FileName}.");
                }

                services.AddSingleton<IPromptBuilder>(new PromptBuilder(PromptBuilder.LoadGuidelines(Console.Error)));
                services.AddHttpClient<ICodeReviewer, GeminiCodeReviewer>((http, sp) => new GeminiCodeReviewer(
                    http,
                    sp.GetRequiredService<IPromptBuilder>(),
                    config,
                    Console.Error));
                break;

            case "claude" or "openai":
                throw new ConfigException(
                    $"Provider '{config.Provider}' isn't implemented yet. Use gemini, or set dry_run to true in {ConfigLoader.FileName}.");

            default:
                throw new ConfigException(
                    $"Unknown provider '{config.Provider}'. Use claude, openai or gemini.");
        }
    }

    /// <summary>
    /// Empty, or still the masked placeholder from config.json.template.
    /// </summary>
    private static bool IsMissingKey(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.StartsWith("YOUR-", StringComparison.OrdinalIgnoreCase);
}
