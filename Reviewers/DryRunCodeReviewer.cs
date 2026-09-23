using PrReviewer.Diffs;
using PrReviewer.Domain;

namespace PrReviewer.Reviewers;

/// <summary>
/// Used when dry_run is true. Makes no API call and returns a fixed
/// placeholder review, so the rest of the pipeline (diff source, filter,
/// printer) can be run at zero cost. The findings cover every severity so
/// the filter visibly has something to drop, and point at real added lines
/// so the printer shows the code, a fix, and a "delete this line".
/// </summary>
public class DryRunCodeReviewer : ICodeReviewer
{
    public Task<CodeReview> ReviewAsync(string diff)
    {
        var lines = DiffParser.Parse(diff).ToList();
        var fileCount = lines.Count(l => l.Text.StartsWith("diff --git "));
        var added = lines.Where(l => l is { File: not null, NewLineNumber: not null } && l.Text.StartsWith('+')).ToList();

        // The Nth added line, or a placeholder location if the diff has fewer.
        (string File, int? Line) At(int index) =>
            index < added.Count ? (added[index].File!, added[index].NewLineNumber) : ("(no file)", null);

        var review = new CodeReview(
            Summary: $"Dry run, no API call made. The diff has {fileCount} changed file(s). "
                     + "Set dry_run to false in config.json for a real review.",
            Findings:
            [
                new(At(0).File, At(0).Line, Severity.Critical, "[dry run] Placeholder critical finding, with a fix.",
                    Suggestion: "[dry run] the fixed line would be here"),
                new(At(1).File, At(1).Line, Severity.Warning, "[dry run] Placeholder warning, fixed by deleting the line.",
                    Suggestion: ""),
                new(At(2).File, At(2).Line, Severity.Suggestion, "[dry run] Placeholder suggestion, with no fix."),
                new(At(0).File, null, Severity.Info, "[dry run] Placeholder info, hidden by the default min_severity.")
            ],
            Verdict: ReviewVerdict.RequestChanges);

        return Task.FromResult(review);
    }
}
