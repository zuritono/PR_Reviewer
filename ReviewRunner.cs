using PrReviewer.Configuration;
using PrReviewer.DiffSources;
using PrReviewer.Output;
using PrReviewer.Reviewers;

namespace PrReviewer;

/// <summary>
/// The pipeline: diff source → reviewer → filter → printer. Every step is
/// injected, so this class is the same whichever diff source or provider
/// the composition root chose.
/// </summary>
public class ReviewRunner(
    IDiffSource diffSource,
    ICodeReviewer reviewer,
    ReviewFilter filter,
    ConsoleReviewPrinter printer,
    AppConfig config,
    TextWriter log)
{
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync()
    {
        var diff = await diffSource.GetDiffAsync();
        if (string.IsNullOrWhiteSpace(diff))
        {
            log.WriteLine("The diff is empty, nothing to review.");
            return 0;
        }

        if (diff.Length > config.MaxTotalContentChars)
        {
            log.WriteLine(
                $"The diff is {diff.Length:N0} characters, over the max_total_content_chars limit of "
                + $"{config.MaxTotalContentChars:N0}. Review a smaller diff or raise the limit in config.json.");
            return 1;
        }

        var review = await reviewer.ReviewAsync(diff);
        printer.Print(filter.Apply(review));
        return 0;
    }
}
