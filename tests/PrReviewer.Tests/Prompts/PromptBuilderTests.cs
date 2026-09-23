using PrReviewer.Prompts;
using PrReviewer.Tests.Helpers;

namespace PrReviewer.Tests.Prompts;

public class PromptBuilderTests
{
    [Fact]
    public void Puts_the_guidelines_first_then_the_numbered_diff()
    {
        var prompt = new PromptBuilder("  MY GUIDELINES  ").BuildPrompt(SampleDiffs.Klelab);

        Assert.StartsWith("MY GUIDELINES", prompt);
        Assert.True(prompt.IndexOf("## Diff to review", StringComparison.Ordinal) > prompt.IndexOf("MY GUIDELINES", StringComparison.Ordinal));
        Assert.Contains("   156 |+$safe_name  = $name;", prompt);
        Assert.Contains("copy the number shown", prompt);
    }

    [Fact]
    public void Loads_guidelines_from_the_given_folder()
    {
        var folder = Directory.CreateTempSubdirectory("prreviewer-tests-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "review_guidelines.md"), "Team rules");

            Assert.Equal("Team rules", PromptBuilder.LoadGuidelines(TextWriter.Null, folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Falls_back_to_built_in_guidelines_when_the_file_is_missing()
    {
        var folder = Directory.CreateTempSubdirectory("prreviewer-tests-").FullName;
        try
        {
            var log = new StringWriter();

            var guidelines = PromptBuilder.LoadGuidelines(log, folder);

            Assert.Contains("experienced teammate", guidelines);
            Assert.Contains("review_guidelines.md not found", log.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
