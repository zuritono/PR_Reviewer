using PrReviewer.Domain;

namespace PrReviewer.Reviewers;

/// <summary>
/// Used when dry_run is true. Makes no API call and returns a fixed
/// placeholder review, so the rest of the pipeline (diff source, filter,
/// printer) can be run at zero cost. The findings cover every severity
/// so the filter visibly has something to drop.
/// </summary>
public class DryRunCodeReviewer : ICodeReviewer
{
    private const string DiffFileHeader = "diff --git ";

    public Task<CodeReview> ReviewAsync(string diff)
    {
        var files = ChangedFiles(diff);
        var file = files.FirstOrDefault() ?? "(no file)";

        var review = new CodeReview(
            Summary: $"Dry run, no API call made. The diff has {files.Count} changed file(s). "
                     + "Set dry_run to false in config.json for a real review.",
            Findings:
            [
                new(file, 1, Severity.Critical, "[dry run] Placeholder critical finding."),
                new(file, 2, Severity.Warning, "[dry run] Placeholder warning."),
                new(file, 3, Severity.Suggestion, "[dry run] Placeholder suggestion."),
                new(file, null, Severity.Info, "[dry run] Placeholder info, hidden by the default min_severity.")
            ],
            Verdict: ReviewVerdict.RequestChanges);

        return Task.FromResult(review);
    }

    /// <summary>
    /// File paths from the "diff --git a/path b/path" headers, using the
    /// b/ (new) side.
    /// </summary>
    private static List<string> ChangedFiles(string diff) =>
        diff.Split('\n')
            .Where(line => line.StartsWith(DiffFileHeader))
            .Select(line => line.TrimEnd('\r'))
            .Select(line => line[(line.LastIndexOf(" b/", StringComparison.Ordinal) + 3)..])
            .ToList();
}
