using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PrReviewer.Configuration;
using PrReviewer.DiffSources;
using PrReviewer.Output;
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
            await services.GetRequiredService<ReviewRunner>().RunAsync();
            return 0;
        }
        catch (Exception ex) when (ex is ConfigException or FileNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
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

        throw config.Provider.ToLowerInvariant() switch
        {
            "claude" or "openai" or "gemini" => new ConfigException(
                $"Provider '{config.Provider}' isn't implemented yet. Set dry_run to true in {ConfigLoader.FileName}."),
            _ => new ConfigException(
                $"Unknown provider '{config.Provider}'. Use claude, openai or gemini.")
        };
    }
}
