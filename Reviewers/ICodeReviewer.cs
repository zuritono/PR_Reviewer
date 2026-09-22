using PrReviewer.Domain;

namespace PrReviewer.Reviewers;

/// <summary>
/// Turns a diff into a structured review. One implementation per AI
/// provider, plus DryRunCodeReviewer; the composition root picks one
/// from config and nothing else needs to know which.
/// </summary>
public interface ICodeReviewer
{
    Task<CodeReview> ReviewAsync(string diff);
}
