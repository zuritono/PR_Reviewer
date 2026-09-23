using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PrReviewer.Configuration;
using PrReviewer.DiffSources;
using PrReviewer.Http;
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
            Console.Error.WriteLine("Usage: PrReviewer <diff-file | github-pr-url>");
            return 1;
        }

        try
        {
            var config = ConfigLoader.Load(Console.Error);
            using var services = BuildServices(config, args[0]);
            return await services.GetRequiredService<ReviewRunner>().RunAsync();
        }
        catch (Exception ex) when (ex is ConfigException or FileNotFoundException
                                       or DiffSourceException or ProviderException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            // The message names the host, e.g. api.github.com or
            // generativelanguage.googleapis.com.
            Console.Error.WriteLine($"Network error: {ex.Message}");
            return 1;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("A web request didn't get an answer in time. Try again, or review a smaller diff.");
            return 1;
        }
    }

    /// <summary>
    /// Composition root: the only place that knows which implementations
    /// are in use. Everything downstream receives interfaces.
    /// </summary>
    private static ServiceProvider BuildServices(AppConfig config, string diffArgument)
    {
        var services = new ServiceCollection();

        services.AddSingleton(config);
        AddDiffSource(services, config, diffArgument);
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
    /// A web address means a GitHub PR; anything else is a diff file.
    /// </summary>
    private static void AddDiffSource(IServiceCollection services, AppConfig config, string argument)
    {
        if (!GitHubPrUrl.LooksLikeUrl(argument))
        {
            services.AddSingleton<IDiffSource>(new LocalFileDiffSource(argument));
            return;
        }

        var pr = GitHubPrUrl.TryParse(argument)
                 ?? throw new ConfigException(
                     $"Not a GitHub pull request link: {argument}. Expected https://github.com/owner/repo/pull/123.");

        services.AddHttpClient<IDiffSource, GitHubPrDiffSource>((http, _) =>
                new GitHubPrDiffSource(http, pr, config.GitHubToken, Console.Error))
            .AddHttpMessageHandler(() => new TransientRetryHandler(Console.Error));
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
                if (config.GeminiApiKey is null)
                {
                    throw new ConfigException(
                        $"No Gemini API key. Set GEMINI_API_KEY or gemini_api_key in {ConfigLoader.FileName}.");
                }

                services.AddSingleton<IPromptBuilder>(new PromptBuilder(PromptBuilder.LoadGuidelines(Console.Error)));
                services.AddHttpClient<ICodeReviewer, GeminiCodeReviewer>((http, sp) => new GeminiCodeReviewer(
                        http,
                        sp.GetRequiredService<IPromptBuilder>(),
                        config,
                        Console.Error))
                    // The timeout covers all attempts together: a review can
                    // take a minute, and the server may ask to wait up to a
                    // minute between retries. The default 100 s is too short.
                    .ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(5))
                    .AddHttpMessageHandler(() => new TransientRetryHandler(Console.Error));
                break;

            case "claude" or "openai":
                throw new ConfigException(
                    $"Provider '{config.Provider}' isn't implemented yet. Use gemini, or set dry_run to true in {ConfigLoader.FileName}.");

            default:
                throw new ConfigException(
                    $"Unknown provider '{config.Provider}'. Use claude, openai or gemini.");
        }
    }
}
