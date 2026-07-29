namespace dotnet_install.Tests;

// The gesture model requires --repo/--github repos to advertise their tools via
// .dotnet-install/.dotnet-install.json (the "tools" array, or the legacy
// exe/project/bundle fields), unless an explicit --project override names the project.
public class RequireAdvertisedTests
{
    [Fact]
    public void ProjectOverride_AlwaysSatisfies()
    {
        Assert.True(GitSource.RequireAdvertised(null, "src/foo/foo.csproj"));
        Assert.True(GitSource.RequireAdvertised(new ToolConfig(), "src/foo/foo.csproj"));
    }

    [Fact]
    public void ManifestTools_Satisfies()
    {
        var config = new ToolConfig
        {
            Tools = [new Tool { Name = "tool", Project = "src/tool/tool.csproj" }]
        };
        Assert.True(GitSource.RequireAdvertised(config, projectOverride: null));
    }

    [Fact]
    public void LegacyManifestProject_Satisfies()
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
    public void ManifestWithoutTools_Fails()
    {
        // A manifest that names a command (exe) but advertises no buildable tool
        // is "not advertised" here.
        var config = new ToolConfig { Name = "demo", Exe = "demo" };
        Assert.False(GitSource.RequireAdvertised(config, projectOverride: null));
    }
}
