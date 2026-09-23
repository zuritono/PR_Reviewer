using PrReviewer.Domain;
using PrReviewer.Reviewers;

namespace PrReviewer.Tests.Reviewers;

public class ReviewResponseParserTests
{
    [Fact]
    public void Parses_a_well_formed_answer()
    {
        var review = ReviewResponseParser.Parse("""
            {"summary":"Adds a lookup.","findings":[
              {"file":"a.cs","line":12,"severity":"Critical","message":"SQL injection.","suggestion":"var cmd = Build(id);"}
            ],"verdict":"RequestChanges"}
            """);

        Assert.Equal("Adds a lookup.", review.Summary);
        Assert.Equal(ReviewVerdict.RequestChanges, review.Verdict);
        var finding = Assert.Single(review.Findings);
        Assert.Equal(new ReviewFinding("a.cs", 12, Severity.Critical, "SQL injection.", "var cmd = Build(id);"), finding);
    }

    [Fact]
    public void Accepts_enum_values_in_any_case()
    {
        var review = ReviewResponseParser.Parse("""
            {"summary":"s","findings":[{"file":"a.cs","line":1,"severity":"warning","message":"m"}],"verdict":"approvewithcomments"}
            """);

        Assert.Equal(Severity.Warning, review.Findings[0].Severity);
        Assert.Equal(ReviewVerdict.ApproveWithComments, review.Verdict);
    }

    [Theory]
    [InlineData("b/src/a.cs", "src/a.cs")]
    [InlineData("./src/a.cs", "src/a.cs")]
    [InlineData("src/a.cs", "src/a.cs")]
    [InlineData("", "(whole diff)")]
    public void Cleans_file_paths(string file, string expected)
    {
        var review = ReviewResponseParser.Parse(
            $$"""{"summary":"s","findings":[{"file":"{{file}}","line":1,"severity":"Warning","message":"m"}],"verdict":"RequestChanges"}""");

        Assert.Equal(expected, review.Findings[0].File);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("null")]
    public void Treats_missing_or_invalid_line_as_no_line(string line)
    {
        var review = ReviewResponseParser.Parse(
            $$"""{"summary":"s","findings":[{"file":"a.cs","line":{{line}},"severity":"Warning","message":"m"}],"verdict":"RequestChanges"}""");

        Assert.Null(review.Findings[0].Line);
    }

    [Fact]
    public void Keeps_empty_suggestion_as_delete_and_null_as_no_fix()
    {
        var review = ReviewResponseParser.Parse("""
            {"summary":"s","findings":[
              {"file":"a.js","line":1,"severity":"Suggestion","message":"delete me","suggestion":""},
              {"file":"a.js","line":2,"severity":"Suggestion","message":"no fix","suggestion":null}
            ],"verdict":"ApproveWithComments"}
            """);

        Assert.Equal("", review.Findings[0].Suggestion);
        Assert.Null(review.Findings[1].Suggestion);
    }

    [Fact]
    public void Skips_findings_without_a_message()
    {
        var review = ReviewResponseParser.Parse("""
            {"summary":"s","findings":[{"file":"a.cs","line":1,"severity":"Warning","message":"  "}],"verdict":"Approve"}
            """);

        Assert.Empty(review.Findings);
    }

    [Theory]
    [InlineData("Sure! Here is my review: looks good.")]
    [InlineData("""{"summary":"s","findings":[],"verdict":"LGTM"}""")]
    [InlineData("""{"summary":"s","verdict":"Approve"}""")]
    public void Falls_back_to_one_warning_with_the_raw_text(string answer)
    {
        var review = ReviewResponseParser.Parse(answer);

        var finding = Assert.Single(review.Findings);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(answer, finding.Message);
        Assert.Equal("(whole diff)", finding.File);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_answer_falls_back_with_a_note(string? answer)
    {
        var review = ReviewResponseParser.Parse(answer);

        Assert.Equal("The model returned an empty response.", Assert.Single(review.Findings).Message);
    }
}
