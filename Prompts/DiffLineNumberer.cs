using System.Text;
using System.Text.RegularExpressions;

namespace PrReviewer.Prompts;

/// <summary>
/// Prefixes every line inside a diff hunk with its line number in the new
/// version of the file, so the model can copy line numbers instead of
/// counting them from the @@ headers (which models get wrong by a few
/// lines). Removed lines have no new line number and get a blank prefix.
/// Everything outside hunks (diff/index/---/+++ headers) is left as-is.
///
///   @@ -18,6 +18,24 @@
///     18 |          _connectionString = connectionString;
///     21 |+    public List&lt;Order&gt; GetOrdersForCustomer(string customerId)
///        |-    old line that was removed
/// </summary>
public static partial class DiffLineNumberer
{
    private const int NumberWidth = 6;

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    public static string Number(string diff)
    {
        var output = new StringBuilder();
        int? newLine = null; // null while outside a hunk

        // Trim the diff's final newline first, or it reads as one extra,
        // empty context line at the end of the last hunk.
        foreach (var rawLine in diff.TrimEnd('\r', '\n').Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("diff --git "))
            {
                newLine = null;
                output.AppendLine(line);
                continue;
            }

            var header = HunkHeader().Match(line);
            if (header.Success)
            {
                newLine = int.Parse(header.Groups[1].Value);
                output.AppendLine(line);
                continue;
            }

            if (newLine is not { } current || line.StartsWith('\\'))
            {
                // Outside a hunk, or "\ No newline at end of file".
                output.AppendLine(line);
                continue;
            }

            if (line.StartsWith('-'))
            {
                output.Append(' ', NumberWidth).Append(" |").AppendLine(line);
            }
            else
            {
                // '+' and ' ' lines exist in the new file. Some tools strip
                // the leading space from empty context lines, so an empty
                // line counts as context too.
                output.Append(current.ToString().PadLeft(NumberWidth)).Append(" |").AppendLine(line);
                newLine = current + 1;
            }
        }

        return output.ToString().TrimEnd();
    }
}
