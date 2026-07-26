namespace dotnet_install.Tests;

/// <summary>
/// Tests for <see cref="ToolConfig.GetTools"/>, the normalization that maps the
/// unified <c>tools</c> array and the legacy <c>exe</c>/<c>project</c>/<c>bundle</c>
/// fields onto a single source-build tool list. A repo is "advertised" when this
/// list is non-empty.
/// </summary>
public class GetToolsTests
{
    [Fact]
    public void Tools_ArrayIsReturnedVerbatim()
    {
        var config = new ToolConfig
        {
            Tools =
            [
                new Tool { Name = "a", Project = "src/a/a.csproj" },
                new Tool { Name = "b", Project = "src/b/b.csproj" }
            ]
        };

        var tools = config.GetTools();

        Assert.Equal(2, tools.Count);
        Assert.Equal("a", tools[0].Name);
        Assert.Equal("src/a/a.csproj", tools[0].Project);
        Assert.Equal("b", tools[1].Name);
    }

    [Fact]
    public void Tools_SingleEntryWithoutProject_IsAdvertised()
    {
        // Go-style: a single tool may omit the project and be auto-detected later.
        var config = new ToolConfig { Tools = [new Tool { Name = "solo" }] };

        Assert.Single(config.GetTools());
        Assert.Null(config.GetTools()[0].Project);
    }

    [Fact]
    public void Legacy_Bundle_NormalizesToTools()
    {
        var config = new ToolConfig
        {
            Bundle =
            [
                new BundleEntry { Project = "src/a/a.csproj" },
                new BundleEntry { Project = "src/b/b.csproj" }
            ]
        };

        var tools = config.GetTools();

        Assert.Equal(2, tools.Count);
        Assert.Equal("src/a/a.csproj", tools[0].Project);
        Assert.Equal("src/b/b.csproj", tools[1].Project);
        Assert.Null(tools[0].Name);
    }

    [Fact]
    public void Legacy_ExePlusProject_NormalizesToSingleNamedTool()
    {
        var config = new ToolConfig { Exe = "mytool", Project = "src/mytool/mytool.csproj" };

        var tools = config.GetTools();

        Assert.Single(tools);
        Assert.Equal("mytool", tools[0].Name);
        Assert.Equal("src/mytool/mytool.csproj", tools[0].Project);
    }

    [Fact]
    public void Legacy_ExeAlone_IsNotAdvertised()
    {
        // `exe` names a command but no buildable source, so it advertises no
        // source-build tool.
        var config = new ToolConfig { Exe = "descriptive-name" };

        Assert.Empty(config.GetTools());
    }

    [Fact]
    public void Empty_IsNotAdvertised()
    {
        Assert.Empty(new ToolConfig().GetTools());
    }

    [Fact]
    public void Tools_TakePrecedenceOverLegacyFields()
    {
        var config = new ToolConfig
        {
            Exe = "legacy",
            Project = "legacy.csproj",
            Bundle = [new BundleEntry { Project = "legacy-bundle.csproj" }],
            Tools = [new Tool { Name = "modern", Project = "modern.csproj" }]
        };

        var tools = config.GetTools();

        Assert.Single(tools);
        Assert.Equal("modern", tools[0].Name);
    }
}
