using System.IO;

namespace dotnet_install.Tests;

// update re-invokes install into the same cached clone the install created, so
// the cache-dir derivation must stay identical across both. github provenance
// (owner/repo) and raw-URL provenance (git) cache differently; a URL must never
// be mis-parsed as owner/repo.
public class RepoCacheDirTests
{
    [Fact]
    public void GitHub_UsesOwnerRepoLayout()
    {
        string dir = GitSource.RepoCacheDirForGitHub("richlander", "dotnet-runtimeinfo");
        Assert.EndsWith(Path.Combine("git-tools", "richlander", "dotnet-runtimeinfo", "repo"), dir);
    }

    [Fact]
    public void Url_UsesHashedGitLayout()
    {
        string dir = GitSource.RepoCacheDirForUrl("https://example.com/owner/repo.git");
        // Raw URLs live under a hashed _git/ bucket, never split into path segments.
        Assert.Contains(Path.Combine("git-tools", "_git"), dir);
        Assert.EndsWith(Path.Combine("repo"), dir);
        Assert.DoesNotContain("example.com", dir);
        Assert.DoesNotContain("https", dir);
    }

    [Fact]
    public void Url_IsDeterministic()
    {
        const string url = "https://gitlab.com/group/tool.git";
        Assert.Equal(GitSource.RepoCacheDirForUrl(url), GitSource.RepoCacheDirForUrl(url));
    }

    [Fact]
    public void Url_AndGitHub_DoNotCollide()
    {
        // A GitHub URL and its owner/repo shorthand are distinct provenances and
        // must not resolve to the same cache directory.
        string urlDir = GitSource.RepoCacheDirForUrl("https://github.com/richlander/dotnet-runtimeinfo.git");
        string ghDir = GitSource.RepoCacheDirForGitHub("richlander", "dotnet-runtimeinfo");
        Assert.NotEqual(urlDir, ghDir);
    }
}
