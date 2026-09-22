namespace PrReviewer.Domain;

/// <summary>
/// A single issue found in the diff. Line is null when the finding applies
/// to the file as a whole, or when the model couldn't pin it to a line.
/// </summary>
public record ReviewFinding(
    string File,
    int? Line,
    Severity Severity,
    string Message
);
