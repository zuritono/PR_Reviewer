using PrReviewer.Configuration;
using PrReviewer.Domain;
using PrReviewer.Output;

namespace PrReviewer.Tests.Output;

public class ReviewFilterTests
{
    private static ReviewFinding Finding(Severity severity, string message = "m") =>
        new("a.cs", 1, severity, message);

    private static CodeReview Review(ReviewVerdict verdict, params ReviewFinding[] findings) =>
        new("summary", findings, verdict);

    [Fact]
    public void Drops_findings_below_min_severity()
    {
        var filter = new ReviewFilter(new AppConfig { MinSeverity = Severity.Warning });

        var result = filter.Apply(Review(ReviewVerdict.RequestChanges,
            Finding(Severity.Info), Finding(Severity.Suggestion), Finding(Severity.Warning), Finding(Severity.Critical)));

        Assert.Equal([Severity.Critical, Severity.Warning], result.Findings.Select(f => f.Severity));
    }

    [Fact]
    public void Keeps_at_most_max_findings_most_severe_first()
    {
        var filter = new ReviewFilter(new AppConfig { MaxFindings = 2 });

        var result = filter.Apply(Review(ReviewVerdict.RequestChanges,
            Finding(Severity.Suggestion), Finding(Severity.Critical), Finding(Severity.Warning)));

        Assert.Equal([Severity.Critical, Severity.Warning], result.Findings.Select(f => f.Severity));
    }

    [Fact]
    public void Keeps_the_model_order_within_the_same_severity()
    {
        var filter = new ReviewFilter(new AppConfig());

        var result = filter.Apply(Review(ReviewVerdict.RequestChanges,
            Finding(Severity.Warning, "first"), Finding(Severity.Critical, "top"), Finding(Severity.Warning, "second")));

        Assert.Equal(["top", "first", "second"], result.Findings.Select(f => f.Message));
    }

    [Fact]
    public void Verdict_is_request_changes_when_a_warning_or_worse_remains()
    {
        var filter = new ReviewFilter(new AppConfig());

        var result = filter.Apply(Review(ReviewVerdict.Approve, Finding(Severity.Warning)));

        Assert.Equal(ReviewVerdict.RequestChanges, result.Verdict);
    }

    [Fact]
    public void Verdict_is_approve_with_comments_when_only_suggestions_remain()
    {
        var filter = new ReviewFilter(new AppConfig());

        var result = filter.Apply(Review(ReviewVerdict.RequestChanges, Finding(Severity.Suggestion)));

        Assert.Equal(ReviewVerdict.ApproveWithComments, result.Verdict);
    }

    [Fact]
    public void Verdict_is_approve_when_filtering_leaves_nothing()
    {
        // The model said "request changes" because of an Info finding the
        // default min_severity hides; the verdict must match what's shown.
        var filter = new ReviewFilter(new AppConfig());

        var result = filter.Apply(Review(ReviewVerdict.RequestChanges, Finding(Severity.Info)));

        Assert.Empty(result.Findings);
        Assert.Equal(ReviewVerdict.Approve, result.Verdict);
    }
}
