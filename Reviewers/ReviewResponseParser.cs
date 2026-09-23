using System.Text.Json;
using System.Text.Json.Serialization;
using PrReviewer.Domain;

namespace PrReviewer.Reviewers;

/// <summary>
/// Turns a provider's JSON answer into a CodeReview. Shared by every
/// provider, since they're all asked for the same shape. Never throws on
/// bad model output: anything that doesn't parse becomes a single
/// Warning finding carrying the raw text, so a malformed response
/// degrades the review instead of crashing the tool.
/// </summary>
public static class ReviewResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static CodeReview Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fallback("The model returned an empty response.");
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ReviewDto>(json, JsonOptions);
            if (dto?.Summary is null || dto.Findings is null || dto.Verdict is null)
            {
                return Fallback(json);
            }

            var findings = dto.Findings
                .Where(f => !string.IsNullOrWhiteSpace(f.Message))
                .Select(f => new ReviewFinding(
                    CleanPath(f.File),
                    f.Line is > 0 ? f.Line : null,
                    f.Severity ?? Severity.Suggestion,
                    f.Message!.Trim(),
                    // Kept as-is apart from trailing whitespace: "" is a
                    // real answer ("delete the line"), null means no fix.
                    f.Suggestion?.TrimEnd()))
                .ToList();

            return new CodeReview(dto.Summary.Trim(), findings, dto.Verdict.Value);
        }
        catch (JsonException)
        {
            return Fallback(json);
        }
    }

    /// <summary>
    /// Models sometimes copy the path with git's "b/" prefix from the
    /// "+++ b/path" header, or with a leading "./".
    /// </summary>
    private static string CleanPath(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return "(whole diff)";
        }

        var path = file.Trim();
        if (path.StartsWith("b/") || path.StartsWith("./"))
        {
            path = path[2..];
        }

        return path;
    }

    public static CodeReview Fallback(string rawText) => new(
        "The model's answer couldn't be read as a structured review; its raw text is below.",
        [new ReviewFinding("(whole diff)", null, Severity.Warning, rawText.Trim())],
        ReviewVerdict.ApproveWithComments);

    private record ReviewDto(string? Summary, List<FindingDto>? Findings, ReviewVerdict? Verdict);

    private record FindingDto(string? File, int? Line, Severity? Severity, string? Message, string? Suggestion);
}
