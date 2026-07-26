namespace dotnet_install.Tests;

// The inside/outside gesture model requires --repo/--github repos to advertise
// their tools via .dotnet-install/.dotnet-install.json (a bundle or a single
// project), unless an explicit --project override names the project.
public class RequireAdvertisedTests
{
    [Fact]
    public void ProjectOverride_AlwaysSatisfies()
    {
        Assert.True(GitSource.RequireAdvertised(null, "src/foo/foo.csproj"));
        Assert.True(GitSource.RequireAdvertised(new ToolConfig(), "src/foo/foo.csproj"));
    }

    [Fact]
    public void ManifestProject_Satisfies()
    {
        var config = new ToolConfig { Project = "src/tool/tool.csproj" };
        Assert.True(GitSource.RequireAdvertised(config, projectOverride: null));
    }

    [Fact]
    public void NoManifest_Fails()
    {
        Assert.False(GitSource.RequireAdvertised(null, projectOverride: null));
    }

    [Fact]
    public void ManifestWithoutProject_Fails()
    {
        // A bundle is handled by the caller before RequireAdvertised runs, so a
        // config with neither a project nor an override is "not advertised" here.
        var config = new ToolConfig { Name = "demo" };
        Assert.False(GitSource.RequireAdvertised(config, projectOverride: null));
    }
}
