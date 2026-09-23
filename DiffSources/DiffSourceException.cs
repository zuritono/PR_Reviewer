namespace PrReviewer.DiffSources;

/// <summary>
/// The diff couldn't be fetched (PR not found, rate limit, diff too large
/// for the API). The message is meant to be shown to the user as-is.
/// </summary>
public class DiffSourceException(string message) : Exception(message);
