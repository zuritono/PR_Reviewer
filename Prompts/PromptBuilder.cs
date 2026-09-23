namespace PrReviewer.Prompts;

/// <summary>
/// Puts review_guidelines.md first, then the diff. The guidelines carry
/// both what to look for and how to write; this class only adds the
/// framing around the diff.
/// </summary>
public class PromptBuilder(string guidelines) : IPromptBuilder
{
    public const string GuidelinesFileName = "review_guidelines.md";

    /// <summary>
    /// Used when review_guidelines.md is missing, so a review still works
    /// and still aims for the short style.
    /// </summary>
    private const string FallbackGuidelines =
        "Review this code diff as an experienced teammate would. Report only real "
        + "problems: bugs, security issues, leftover debug code. Keep each comment to one "
        + "or two short, plain sentences. An empty list of findings is a fine review.";

    public string BuildPrompt(string diff) =>
        $"""
        {guidelines.Trim()}

        ## Diff to review

        Review only the changes in this diff. Lines starting with + were added and
        lines starting with - were removed. Line numbers refer to the new version of
        the file, counted from the @@ hunk headers.

        ```diff
        {diff.TrimEnd()}
        ```
        """;

    /// <summary>
    /// Reads review_guidelines.md from the current directory, falling back
    /// to a short built-in instruction if the file isn't there.
    /// </summary>
    public static string LoadGuidelines(TextWriter log)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), GuidelinesFileName);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        log.WriteLine($"{GuidelinesFileName} not found, using built-in minimal guidelines.");
        return FallbackGuidelines;
    }
}
