using PrReviewer.Domain;

namespace PrReviewer.Output;

/// <summary>
/// Renders a review in a fixed format decided here, never by the model:
/// the summary, then one block per finding, then the verdict.
///
///   script.js:31 — Should fix
///     The check is inverted, so the form resets on failure.
///     - if (!result.success) {
///     + if (result.success) {
///
/// The "-" line is the code the finding points at (from the diff); the "+"
/// line is the model's fix, shown only when it gave one.
/// </summary>
public class ConsoleReviewPrinter(TextWriter output)
{
    private const string Indent = "  ";

    public void Print(CodeReview review)
    {
        output.WriteLine(review.Summary);
        output.WriteLine();

        foreach (var finding in review.Findings)
        {
            PrintFinding(finding);
            output.WriteLine();
        }

        output.WriteLine(VerdictText(review.Verdict));
    }

    private void PrintFinding(ReviewFinding finding)
    {
        var location = finding.Line is { } line ? $"{finding.File}:{line}" : finding.File;
        output.WriteLine($"{location} — {StatusText(finding.Severity)}");
        output.WriteLine($"{Indent}{finding.Message}");

        if (finding.CodeLine is { } code)
        {
            output.WriteLine($"{Indent}- {code.Trim()}");
        }

        switch (finding.Suggestion)
        {
            case null:
                break;
            case "" when finding.CodeLine is not null:
                output.WriteLine($"{Indent}+ (delete this line)");
                break;
            case "":
                break;
            // A "fix" identical to the current line adds nothing.
            case var fix when fix.Trim() == finding.CodeLine?.Trim():
                break;
            case var fix:
                foreach (var fixLine in Dedent(fix))
                {
                    output.WriteLine($"{Indent}+ {fixLine}");
                }
                break;
        }
    }

    /// <summary>
    /// Removes the indentation all lines share, keeping the relative
    /// indentation inside a multi-line fix.
    /// </summary>
    private static IEnumerable<string> Dedent(string code)
    {
        var lines = code.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).ToList();
        var common = lines.Where(l => l.Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return lines.Select(l => l.Length >= common ? l[common..] : l);
    }

    /// <summary>
    /// Per-finding status, so each one can be acted on separately.
    /// </summary>
    private static string StatusText(Severity severity) => severity switch
    {
        Severity.Critical => "Must fix",
        Severity.Warning => "Should fix",
        Severity.Suggestion => "Optional",
        Severity.Info => "FYI",
        _ => severity.ToString()
    };

    private static string VerdictText(ReviewVerdict verdict) => verdict switch
    {
        ReviewVerdict.Approve => "Approve.",
        ReviewVerdict.ApproveWithComments => "Approve with comments.",
        ReviewVerdict.RequestChanges => "Request changes.",
        _ => verdict.ToString()
    };
}
