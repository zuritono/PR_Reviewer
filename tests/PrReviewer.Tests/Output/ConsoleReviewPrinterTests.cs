using PrReviewer.Domain;
using PrReviewer.Output;

namespace PrReviewer.Tests.Output;

public class ConsoleReviewPrinterTests
{
    private static string[] Print(params ReviewFinding[] findings) =>
        Print(new CodeReview("One-line summary.", findings, ReviewVerdict.RequestChanges));

    private static string[] Print(CodeReview review)
    {
        var output = new StringWriter();
        new ConsoleReviewPrinter(output).Print(review);
        return output.ToString().Split(Environment.NewLine);
    }

    [Fact]
    public void Prints_location_status_message_bug_line_and_fix()
    {
        var lines = Print(new ReviewFinding("script.js", 31, Severity.Warning, "The check is inverted.",
            Suggestion: "      if (result.success) {", CodeLine: "      if (!result.success) {"));

        Assert.Equal(
            ["One-line summary.", "",
             "script.js:31 — Should fix",
             "  The check is inverted.",
             "  - if (!result.success) {",
             "  + if (result.success) {",
             "", "Request changes.", ""],
            lines);
    }

    [Theory]
    [InlineData(Severity.Critical, "Must fix")]
    [InlineData(Severity.Warning, "Should fix")]
    [InlineData(Severity.Suggestion, "Optional")]
    [InlineData(Severity.Info, "FYI")]
    public void Shows_a_status_per_finding(Severity severity, string status)
    {
        var lines = Print(new ReviewFinding("a.cs", 5, severity, "m"));

        Assert.Contains($"a.cs:5 — {status}", lines);
    }

    [Fact]
    public void Empty_suggestion_means_delete_the_line()
    {
        var lines = Print(new ReviewFinding("a.js", 29, Severity.Suggestion, "Leftover debug output.",
            Suggestion: "", CodeLine: "console.log(x);"));

        Assert.Contains("  - console.log(x);", lines);
        Assert.Contains("  + (delete this line)", lines);
    }

    [Fact]
    public void No_suggestion_shows_only_the_bug_line()
    {
        var lines = Print(new ReviewFinding("a.cs", 3, Severity.Warning, "No test covers this.",
            Suggestion: null, CodeLine: "var total = Sum();"));

        Assert.Contains("  - var total = Sum();", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("  + "));
    }

    [Fact]
    public void Fix_identical_to_the_current_line_is_hidden()
    {
        var lines = Print(new ReviewFinding("a.cs", 3, Severity.Suggestion, "m",
            Suggestion: "  x = 1;", CodeLine: "x = 1;"));

        Assert.DoesNotContain(lines, l => l.StartsWith("  + "));
    }

    [Fact]
    public void Multi_line_fix_keeps_its_relative_indentation()
    {
        var lines = Print(new ReviewFinding("a.cs", 3, Severity.Warning, "m",
            Suggestion: "        if (x)\n        {\n            y();\n        }", CodeLine: "if (x) y();"));

        Assert.Equal(["  + if (x)", "  + {", "  +     y();", "  + }"], lines.Where(l => l.StartsWith("  + ")));
    }

    [Fact]
    public void File_level_finding_has_no_line_number_and_no_code()
    {
        var lines = Print(new ReviewFinding("contact.php", null, Severity.Warning, "No tests for this file."));

        Assert.Contains("contact.php — Should fix", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("  - ") || l.StartsWith("  + "));
    }

    [Theory]
    [InlineData(ReviewVerdict.Approve, "Approve.")]
    [InlineData(ReviewVerdict.ApproveWithComments, "Approve with comments.")]
    [InlineData(ReviewVerdict.RequestChanges, "Request changes.")]
    public void Ends_with_the_verdict(ReviewVerdict verdict, string text)
    {
        var lines = Print(new CodeReview("s", [], verdict));

        Assert.Equal(["s", "", text, ""], lines);
    }
}
