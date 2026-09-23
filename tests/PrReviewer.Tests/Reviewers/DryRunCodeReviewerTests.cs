using PrReviewer.Domain;
using PrReviewer.Reviewers;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests.Reviewers;

public class DryRunCodeReviewerTests
{
    [Fact]
    public async Task Makes_placeholder_findings_on_real_added_lines()
    {
        var review = await new DryRunCodeReviewer().ReviewAsync(SampleDiffs.Klelab);

        Assert.Contains("Dry run", review.Summary);
        Assert.Contains("2 changed file(s)", review.Summary);
        Assert.Equal(
            [("contact.php", (int?)156), ("script.js", 29), ("script.js", 31)],
            review.Findings.Take(3).Select(f => (f.File, f.Line)));
    }

    [Fact]
    public async Task Covers_every_severity_and_every_kind_of_fix()
    {
        var review = await new DryRunCodeReviewer().ReviewAsync(SampleDiffs.Klelab);

        Assert.Equal([Severity.Critical, Severity.Warning, Severity.Suggestion, Severity.Info], review.Findings.Select(f => f.Severity));
        Assert.NotEmpty(review.Findings[0].Suggestion!);
        Assert.Equal("", review.Findings[1].Suggestion);
        Assert.Null(review.Findings[2].Suggestion);
    }

    [Fact]
    public async Task Works_on_a_diff_with_no_added_lines()
    {
        var review = await new DryRunCodeReviewer().ReviewAsync("diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n unchanged");

        Assert.All(review.Findings, f => Assert.Equal("(no file)", f.File));
    }
}
