using System.Text;
using PrReviewer.Diffs;

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
public static class DiffLineNumberer
{
    private const int NumberWidth = 6;

    public static string Number(string diff)
    {
        var output = new StringBuilder();

        foreach (var line in DiffParser.Parse(diff))
        {
            if (line.NewLineNumber is { } number)
            {
                output.Append(number.ToString().PadLeft(NumberWidth)).Append(" |").AppendLine(line.Text);
            }
            else if (line.IsRemoved)
            {
                output.Append(' ', NumberWidth).Append(" |").AppendLine(line.Text);
            }
            else
            {
                output.AppendLine(line.Text);
            }
        }

        return output.ToString().TrimEnd();
    }
}
