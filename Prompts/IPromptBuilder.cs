namespace PrReviewer.Prompts;

/// <summary>
/// Combines the diff with the review guidelines into the one prompt
/// string every provider sends. Shared so the prompt is written once,
/// not once per provider.
/// </summary>
public interface IPromptBuilder
{
    string BuildPrompt(string diff);
}
