using PrReviewer.Configuration;
using PrReviewer.Domain;

namespace PrReviewer.Output;

/// <summary>
/// Applies the rules the model can't be trusted to follow on its own:
/// drops findings below min_severity, keeps at most max_findings (most
/// severe first), and recomputes the verdict from what's left so the
/// verdict always matches the findings shown.
/// </summary>
public class ReviewFilter(AppConfig config)
{
    public CodeReview Apply(CodeReview review)
    {
        // OrderByDescending is stable, so findings of equal severity keep
        // the order the model returned them in.
        var findings = review.Findings
            .Where(f => f.Severity >= config.MinSeverity)
            .OrderByDescending(f => f.Severity)
            .Take(config.MaxFindings)
            .ToList();

        return review with { Findings = findings, Verdict = VerdictFor(findings) };
    }

    private static ReviewVerdict VerdictFor(IReadOnlyList<ReviewFinding> findings)
    {
        if (findings.Any(f => f.Severity >= Severity.Warning))
        {
            return ReviewVerdict.RequestChanges;
        }

        return findings.Count > 0 ? ReviewVerdict.ApproveWithComments : ReviewVerdict.Approve;
    }
}
