using PrReviewer.DiffSources;

namespace PrReviewer.Tests.DiffSources;

public class GitHubPrUrlTests
{
    [Theory]
    [InlineData("https://github.com/octocat/Hello-World/pull/1", "octocat", "Hello-World", 1)]
    [InlineData("https://github.com/dotnet/runtime/pull/12345/files", "dotnet", "runtime", 12345)]
    [InlineData("https://github.com/dotnet/runtime/pull/12345/files#diff-abc", "dotnet", "runtime", 12345)]
    [InlineData("https://github.com/a-b/repo.name_x/pull/7?w=1", "a-b", "repo.name_x", 7)]
    [InlineData("https://github.com/owner/repo/pull/9.diff", "owner", "repo", 9)]
    [InlineData("HTTPS://WWW.GITHUB.COM/Owner/Repo/pull/3/", "Owner", "Repo", 3)]
    public void Parses_pull_request_links(string url, string owner, string repo, int number)
    {
        Assert.Equal(new GitHubPrUrl(owner, repo, number), GitHubPrUrl.TryParse(url));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/issues/5")]
    [InlineData("https://github.com/owner/repo/pull/abc")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("https://gitlab.com/owner/repo/-/merge_requests/1")]
    public void Rejects_links_that_are_not_pull_requests(string url)
    {
        Assert.Null(GitHubPrUrl.TryParse(url));
    }

    [Theory]
    [InlineData("https://github.com/o/r/pull/1", true)]
    [InlineData("http://example.com", true)]
    [InlineData("pr.diff", false)]
    [InlineData(@"C:\Temp\pr.diff", false)]
    public void Tells_urls_from_file_names(string argument, bool isUrl)
    {
        Assert.Equal(isUrl, GitHubPrUrl.LooksLikeUrl(argument));
    }

    [Fact]
    public void Formats_as_owner_repo_number()
    {
        Assert.Equal("octocat/Hello-World#1", new GitHubPrUrl("octocat", "Hello-World", 1).ToString());
    }
}
