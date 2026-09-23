using System.Text.RegularExpressions;

namespace PrReviewer.DiffSources;

/// <summary>
/// A GitHub pull request link, e.g. https://github.com/owner/repo/pull/123.
/// Links to a PR's tabs (/files, /commits), with a query string or anchor,
/// or with .diff on the end are accepted too, since those are what people
/// copy from the browser.
/// </summary>
public partial record GitHubPrUrl(string Owner, string Repo, int Number)
{
    [GeneratedRegex(
        @"^https?://(?:www\.)?github\.com/(?<owner>[A-Za-z0-9-]+)/(?<repo>[A-Za-z0-9._-]+)/pull/(?<number>\d+)(?:\.diff|\.patch)?(?:/[^?#]*)?(?:[?#].*)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>
    /// True for anything that looks like a web address, so it's treated as
    /// a URL (and rejected with a clear message if it isn't a GitHub PR)
    /// rather than as a file name.
    /// </summary>
    public static bool LooksLikeUrl(string argument) =>
        argument.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || argument.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static GitHubPrUrl? TryParse(string url)
    {
        var match = Pattern().Match(url.Trim());
        return match.Success
            ? new GitHubPrUrl(match.Groups["owner"].Value, match.Groups["repo"].Value, int.Parse(match.Groups["number"].Value))
            : null;
    }

    public override string ToString() => $"{Owner}/{Repo}#{Number}";
}
