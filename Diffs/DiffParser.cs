using System.Text.RegularExpressions;

namespace PrReviewer.Diffs;

/// <summary>
/// One line of a unified diff, with what the parser knows about it.
/// </summary>
/// <param name="Text">The raw diff line, without its line ending.</param>
/// <param name="File">Path of the file this line belongs to (the new side),
/// or null outside any file, or for a deleted file.</param>
/// <param name="NewLineNumber">Line number in the new version of the file, for
/// added and unchanged lines inside a hunk; null for removed lines and for
/// headers.</param>
/// <param name="IsRemoved">True for a "-" line inside a hunk.</param>
public record DiffLine(string Text, string? File, int? NewLineNumber, bool IsRemoved)
{
    /// <summary>The code itself, without the leading +, - or space.</summary>
    public string Code => NewLineNumber is not null || IsRemoved ? Text[Math.Min(1, Text.Length)..] : Text;
}

/// <summary>
/// Walks a unified diff (git diff, or a GitHub PR diff) and works out, for
/// every line inside a hunk, which file it belongs to and its line number in
/// the new version of that file. Shared by the prompt (which shows the
/// numbers to the model) and the printer (which looks up the line a finding
/// points at).
/// </summary>
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    public static IEnumerable<DiffLine> Parse(string diff)
    {
        string? file = null;
        int? newLine = null; // null while outside a hunk

        // Trim the diff's final newline first, or it reads as one extra,
        // empty context line at the end of the last hunk.
        foreach (var rawLine in diff.TrimEnd('\r', '\n').Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("diff --git "))
            {
                newLine = null;
                var bSide = line.LastIndexOf(" b/", StringComparison.Ordinal);
                file = bSide >= 0 ? line[(bSide + 3)..] : null;
                yield return new DiffLine(line, file, null, false);
                continue;
            }

            if (newLine is null && line.StartsWith("+++ "))
            {
                // The new side's name; /dev/null means the file was deleted.
                file = line.StartsWith("+++ b/") ? line[6..] : null;
                yield return new DiffLine(line, file, null, false);
                continue;
            }

            var header = HunkHeader().Match(line);
            if (header.Success)
            {
                newLine = int.Parse(header.Groups[1].Value);
                yield return new DiffLine(line, file, null, false);
                continue;
            }

            if (newLine is not { } current || line.StartsWith('\\'))
            {
                // Outside a hunk, or "\ No newline at end of file".
                yield return new DiffLine(line, file, null, false);
                continue;
            }

            if (line.StartsWith('-'))
            {
                yield return new DiffLine(line, file, null, true);
                continue;
            }

            // '+' and ' ' lines exist in the new file. Some tools strip the
            // leading space from empty context lines, so an empty line
            // counts as context too.
            yield return new DiffLine(line, file, current, false);
            newLine = current + 1;
        }
    }

    /// <summary>
    /// Every line that exists in the new version of a file, keyed by file
    /// and line number, for looking up the line a finding points at.
    /// </summary>
    public static IReadOnlyDictionary<(string File, int Line), string> NewLines(string diff) =>
        Parse(diff)
            .Where(l => l is { File: not null, NewLineNumber: not null })
            .GroupBy(l => (l.File!, l.NewLineNumber!.Value))
            .ToDictionary(g => g.Key, g => g.First().Code);
}
