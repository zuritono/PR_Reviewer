namespace PrReviewer.DiffSources;

/// <summary>
/// Where the diff comes from. v1 reads a local file; a GitHub PR source
/// is planned for v2.
/// </summary>
public interface IDiffSource
{
    Task<string> GetDiffAsync();
}
