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

        Review only the changes in this diff. Each line inside a hunk starts with its
        line number in the new version of the file, then "|", then the diff line.
        After the "|", + means added, - means removed, and a space means unchanged.
        Removed lines have no number. For a finding's line, copy the number shown
        on the line; don't count lines yourself.

        ```
        {DiffLineNumberer.Number(diff)}
        ```
        """;

    /// <summary>
    /// Reads review_guidelines.md from the app's own folder (the build
    /// copies it there), falling back to a short built-in instruction if
    /// the file isn't there.
    /// </summary>
    /// <param name="directory">Defaults to the app's own folder; tests pass
    /// a temporary folder.</param>
    public static string LoadGuidelines(TextWriter log, string? directory = null)
    {
        directory ??= AppContext.BaseDirectory;
        var path = Path.Combine(directory, GuidelinesFileName);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        log.WriteLine($"{GuidelinesFileName} not found in {directory}, using built-in minimal guidelines.");
        return FallbackGuidelines;
    }
}
