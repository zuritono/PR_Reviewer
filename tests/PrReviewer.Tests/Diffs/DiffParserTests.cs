using PrReviewer.Diffs;
using PrReviewer.Prompts;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests.Diffs;

public class DiffParserTests
{
    [Fact]
    public void Numbers_lines_from_the_hunk_header_of_the_new_file()
    {
        var lines = DiffParser.NewLines(SampleDiffs.Mixed);

        Assert.Equal("keep10", lines[("src/A.cs", 10)]);
        Assert.Equal("new11", lines[("src/A.cs", 11)]);
        Assert.Equal("keep13", lines[("src/A.cs", 13)]);
    }

    [Fact]
    public void Removed_lines_have_no_new_line_number()
    {
        var removed = DiffParser.Parse(SampleDiffs.Mixed).Single(l => l.Text == "-old11");

        Assert.True(removed.IsRemoved);
        Assert.Null(removed.NewLineNumber);
    }

    [Fact]
    public void Empty_context_line_without_leading_space_still_counts()
    {
        // Line 12 is the empty line between "+new11" and " keep13".
        var lines = DiffParser.NewLines(SampleDiffs.Mixed);

        Assert.Equal("", lines[("src/A.cs", 12)]);
    }

    [Fact]
    public void Second_hunk_restarts_numbering_from_its_own_header()
    {
        var lines = DiffParser.NewLines(SampleDiffs.Mixed);

        Assert.Equal("keep50", lines[("src/A.cs", 50)]);
        Assert.Equal("added51", lines[("src/A.cs", 51)]);
        Assert.Equal("keep52", lines[("src/A.cs", 52)]);
    }

    [Fact]
    public void No_newline_marker_is_not_a_code_line()
    {
        var lines = DiffParser.NewLines(SampleDiffs.Mixed);

        Assert.False(lines.ContainsKey(("src/A.cs", 53)));
    }

    [Fact]
    public void New_file_is_numbered_from_one()
    {
        var lines = DiffParser.NewLines(SampleDiffs.Mixed);

        Assert.Equal("line1", lines[("db/B.sql", 1)]);
        Assert.Equal("line2", lines[("db/B.sql", 2)]);
    }

    [Fact]
    public void Deleted_file_has_no_new_lines()
    {
        var deleted = DiffParser.Parse(SampleDiffs.Mixed).Where(l => l.Text.StartsWith("-gone")).ToList();

        Assert.Equal(2, deleted.Count);
        Assert.All(deleted, l => Assert.Null(l.File));
        Assert.DoesNotContain(DiffParser.NewLines(SampleDiffs.Mixed).Keys, k => k.File == "Old.cs");
    }

    [Fact]
    public void Trailing_newline_does_not_add_a_phantom_line()
    {
        var lines = DiffParser.NewLines(SampleDiffs.Mixed);

        Assert.False(lines.ContainsKey(("db/B.sql", 3)));
    }

    [Fact]
    public void Windows_line_endings_give_the_same_result()
    {
        var unix = DiffParser.NewLines(SampleDiffs.Mixed);
        var windows = DiffParser.NewLines(SampleDiffs.Mixed.Replace("\n", "\r\n"));

        Assert.Equal(unix, windows);
    }

    [Fact]
    public void Code_strips_the_diff_marker_but_keeps_indentation()
    {
        var lines = DiffParser.NewLines(SampleDiffs.Klelab);

        Assert.Equal("      console.log(\"form result\", result);", lines[("script.js", 29)]);
    }

    [Fact]
    public void Real_pr_diff_puts_each_planted_bug_on_the_right_line()
    {
        var lines = DiffParser.NewLines(SampleDiffs.Klelab);

        Assert.Equal("$safe_name  = $name;", lines[("contact.php", 156)]);
        Assert.Contains("console.log", lines[("script.js", 29)]);
        Assert.Contains("if (!result.success)", lines[("script.js", 31)]);
    }
}

public class DiffLineNumbererTests
{
    [Fact]
    public void Prefixes_new_lines_with_their_number_and_removed_lines_with_blanks()
    {
        var numbered = DiffLineNumberer.Number(SampleDiffs.Mixed).Split(Environment.NewLine);

        Assert.Contains("    10 | keep10", numbered);
        Assert.Contains("       |-old11", numbered);
        Assert.Contains("    11 |+new11", numbered);
    }

    [Fact]
    public void Leaves_headers_unchanged()
    {
        var numbered = DiffLineNumberer.Number(SampleDiffs.Mixed).Split(Environment.NewLine);

        Assert.Contains("diff --git a/src/A.cs b/src/A.cs", numbered);
        Assert.Contains("+++ b/src/A.cs", numbered);
        Assert.Contains("@@ -10,4 +10,4 @@ class A", numbered);
    }
}
