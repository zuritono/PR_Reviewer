namespace PrReviewer.DiffSources;

/// <summary>
/// Reads a diff from a file on disk, e.g. one produced by
/// <c>git diff &gt; changes.diff</c>.
/// </summary>
public class LocalFileDiffSource(string path) : IDiffSource
{
    public async Task<string> GetDiffAsync()
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Diff file not found: {path}", path);
        }

        return await File.ReadAllTextAsync(path);
    }
}
