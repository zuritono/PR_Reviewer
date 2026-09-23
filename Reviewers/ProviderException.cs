namespace PrReviewer.Reviewers;

/// <summary>
/// The provider's API refused or failed the request (bad key, unknown
/// model, quota exceeded, server error). The message is meant to be shown
/// to the user as-is.
/// </summary>
public class ProviderException(string message) : Exception(message);
