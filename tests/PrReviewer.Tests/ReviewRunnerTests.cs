using PrReviewer.Configuration;
using PrReviewer.DiffSources;
using PrReviewer.Domain;
using PrReviewer.Output;
using PrReviewer.Reviewers;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests;

/// <summary>
/// The whole pipeline (diff → reviewer → filter → printer) with a fixed
/// diff and a reviewer that returns a canned answer: no files, no network.
/// </summary>
public class ReviewRunnerTests
{
    private sealed class FixedDiff(string diff) : IDiffSource
    {
        public Task<string> GetDiffAsync() => Task.FromResult(diff);
    }

    private sealed class FixedReviewer(CodeReview review) : ICodeReviewer
    {
        public int Calls { get; private set; }

        public Task<CodeReview> ReviewAsync(string diff)
        {
            Calls++;
            return Task.FromResult(review);
        }
    }

    private static readonly CodeReview KlelabReview = new("Three bugs.",
    [
        new ReviewFinding("contact.php", 156, Severity.Critical, "Header injection.",
            Suggestion: "$safe_name  = str_replace([\"\\r\", \"\\n\"], '', $name);"),
        new ReviewFinding("script.js", 29, Severity.Suggestion, "Leftover debug output.", Suggestion: ""),
        new ReviewFinding("script.js", 31, Severity.Warning, "Inverted check.", Suggestion: "      if (result.success) {")
    ], ReviewVerdict.RequestChanges);

    private static async Task<(int ExitCode, string Output, string Log, FixedReviewer Reviewer)> Run(
        string diff, CodeReview review, AppConfig? config = null)
    {
        config ??= new AppConfig();
        var output = new StringWriter();
        var log = new StringWriter();
        var reviewer = new FixedReviewer(review);
        var runner = new ReviewRunner(new FixedDiff(diff), reviewer, new ReviewFilter(config),
            new ConsoleReviewPrinter(output), config, log);
        return (await runner.RunAsync(), output.ToString(), log.ToString(), reviewer);
    }

    [Fact]
    public async Task Prints_each_finding_with_its_line_from_the_diff_and_fix()
    {
        var (exitCode, output, _, _) = await Run(SampleDiffs.Klelab, KlelabReview);

        Assert.Equal(0, exitCode);
        var expected = string.Join(Environment.NewLine,
            "Three bugs.",
            "",
            "contact.php:156 — Must fix",
            "  Header injection.",
            "  - $safe_name  = $name;",
            "  + $safe_name  = str_replace([\"\\r\", \"\\n\"], '', $name);",
            "",
            "script.js:31 — Should fix",
            "  Inverted check.",
            "  - if (!result.success) {",
            "  + if (result.success) {",
            "",
            "script.js:29 — Optional",
            "  Leftover debug output.",
            "  - console.log(\"form result\", result);",
            "  + (delete this line)",
            "",
            "Request changes.",
            "");
        Assert.Equal(expected, output);
    }

    [Fact]
    public async Task Finding_pointing_outside_the_diff_has_no_code_line()
    {
        var review = new CodeReview("s", [new ReviewFinding("script.js", 999, Severity.Warning, "Somewhere else.")],
            ReviewVerdict.RequestChanges);

        var (_, output, _, _) = await Run(SampleDiffs.Klelab, review);

        Assert.Contains("script.js:999 — Should fix", output);
        Assert.DoesNotContain("  - ", output);
    }

    [Fact]
    public async Task Empty_diff_is_not_sent_for_review()
    {
        var (exitCode, output, log, reviewer) = await Run("   ", KlelabReview);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, reviewer.Calls);
        Assert.Empty(output);
        Assert.Contains("The diff is empty", log);
    }

    [Fact]
    public async Task Diff_over_the_size_limit_is_refused_before_any_review()
    {
        var (exitCode, _, log, reviewer) = await Run(SampleDiffs.Klelab, KlelabReview, new AppConfig { MaxTotalContentChars = 100 });

        Assert.Equal(1, exitCode);
        Assert.Equal(0, reviewer.Calls);
        Assert.Contains("over the max_total_content_chars limit of 100", log);
    }
}
