using PrReviewer.Domain;

namespace PrReviewer.Output;

/// <summary>
/// Renders a review in a fixed, compact format: the summary, one line per
/// finding (file:line — message), then the verdict. The layout is decided
/// here, never by the model.
/// </summary>
public class ConsoleReviewPrinter(TextWriter output)
{
    public void Print(CodeReview review)
    {
        output.WriteLine(review.Summary);
        output.WriteLine();

        foreach (var finding in review.Findings)
        {
            var location = finding.Line is { } line ? $"{finding.File}:{line}" : finding.File;
            output.WriteLine($"{location} — {finding.Message}");
        }

        if (review.Findings.Count > 0)
        {
            output.WriteLine();
        }

        output.WriteLine(VerdictText(review.Verdict));
    }

    private static string VerdictText(ReviewVerdict verdict) => verdict switch
    {
        ReviewVerdict.Approve => "Approve.",
        ReviewVerdict.ApproveWithComments => "Approve with comments.",
        ReviewVerdict.RequestChanges => "Request changes.",
        _ => verdict.ToString()
    };
}
