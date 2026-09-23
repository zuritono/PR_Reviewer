namespace PrReviewer.Domain;

/// <summary>
/// A single issue found in the diff. Line is null when the finding applies
/// to the file as a whole, or when the model couldn't pin it to a line.
/// </summary>
/// <param name="Suggestion">The model's fixed version of the line (one or a
/// few lines of code). Null when there's no small, concrete fix; an empty
/// string means "delete the line".</param>
/// <param name="CodeLine">The line the finding points at, looked up in the
/// diff by the tool (not copied by the model, which could misquote it).
/// Null when the line isn't in the diff.</param>
public record ReviewFinding(
    string File,
    int? Line,
    Severity Severity,
    string Message,
    string? Suggestion = null,
    string? CodeLine = null
);
