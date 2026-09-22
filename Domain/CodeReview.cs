namespace PrReviewer.Domain;

/// <summary>
/// The complete result of reviewing one diff. Every ICodeReviewer
/// implementation returns this shape, whichever provider produced it.
/// </summary>
public record CodeReview(
    string Summary,
    IReadOnlyList<ReviewFinding> Findings,
    ReviewVerdict Verdict
);
