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
    TextWriter log)
{
    public async Task RunAsync()
    {
        var diff = await diffSource.GetDiffAsync();
        if (string.IsNullOrWhiteSpace(diff))
        {
            log.WriteLine("The diff is empty, nothing to review.");
            return;
        }

        var review = await reviewer.ReviewAsync(diff);
        printer.Print(filter.Apply(review));
    }
}
