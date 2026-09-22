namespace PrReviewer.Domain;

/// <summary>
/// How serious a finding is, ordered from least to most severe.
/// </summary>
public enum Severity
{
    Info,
    Suggestion,
    Warning,
    Critical
}
