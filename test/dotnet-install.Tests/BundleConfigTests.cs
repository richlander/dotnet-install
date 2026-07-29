namespace dotnet_install.Tests;

/// <summary>
/// Tests for parsing the repo-advertised "bundle" toolset from .dotnet-install.json.
/// </summary>
public class BundleConfigTests : IDisposable
{
    readonly string _tempDir;

    public BundleConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-bundle-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    void WriteRepoManifest(string json)
    {
        string dir = Path.Combine(_tempDir, ToolConfig.RepoDirName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ToolConfig.FileName), json);
    }

    void WriteColocated(string json) =>
        File.WriteAllText(Path.Combine(_tempDir, ToolConfig.FileName), json);

    [Fact]
    public void ReadFromRepo_ParsesBundleEntries()
    {
        WriteRepoManifest("""
        {
          "version": 3,
          "name": "my-toolset",
          "bundle": [
            { "project": "src/tool-a/tool-a.csproj" },
            { "project": "src/tool-b/tool-b.csproj" }
          ]
        }
        """);

        var config = ToolConfig.ReadFromRepo(_tempDir);

        Assert.NotNull(config);
        Assert.Equal(3, config.Version);
        Assert.Equal("my-toolset", config.Name);
        Assert.NotNull(config.Bundle);
        Assert.Equal(2, config.Bundle.Count);
        Assert.Equal("src/tool-a/tool-a.csproj", config.Bundle[0].Project);
        Assert.Equal("src/tool-b/tool-b.csproj", config.Bundle[1].Project);
    }

    [Fact]
    public void ReadFromRepo_IgnoresBareRootFile()
    {
        // A .dotnet-install.json at the repo root must NOT be treated as the repo
        // manifest; only .dotnet-install/.dotnet-install.json is honored.
        WriteColocated("""
        { "bundle": [ { "project": "root.csproj" } ] }
        """);

        Assert.Null(ToolConfig.ReadFromRepo(_tempDir));
    }

    [Fact]
    public void Read_ParsesColocatedFile()
    {
        WriteColocated("""
        { "exe": "solo-tool" }
        """);

        var config = ToolConfig.Read(_tempDir);

        Assert.NotNull(config);
        Assert.Equal("solo-tool", config.Exe);
    }

    [Fact]
    public void ReadFromRepo_ReturnsNull_WhenAbsent()
    {
        Assert.Null(ToolConfig.ReadFromRepo(_tempDir));
    }

    [Fact]
    public void ReadFromRepo_ParsesSingleToolProject()
    {
        // The shape `dotnet-install .` consumes for a single-tool repo whose
        // project lives in a subdirectory (e.g. dotnet-inspect).
        WriteRepoManifest("""
        {
          "exe": "mytool",
          "project": "src/mytool/mytool.csproj"
        }
        """);

        var config = ToolConfig.ReadFromRepo(_tempDir);

        Assert.NotNull(config);
        Assert.Equal("mytool", config.Exe);
        Assert.Equal("src/mytool/mytool.csproj", config.Project);
        Assert.Null(config.Bundle);
    }

    [Fact]
    public void ReadFromRepo_ParsesToolsArray()
    {
        WriteRepoManifest("""
        {
          "version": 3,
          "name": "my-toolset",
          "tools": [
            { "name": "tool-a", "project": "src/tool-a/tool-a.csproj" },
            { "name": "tool-b", "project": "src/tool-b/tool-b.csproj" }
          ]
        }
        """);

        var config = ToolConfig.ReadFromRepo(_tempDir);

        Assert.NotNull(config);
        Assert.Equal(3, config.Version);
        Assert.Equal("my-toolset", config.Name);
        Assert.NotNull(config.Tools);
        Assert.Equal(2, config.Tools.Count);
        Assert.Equal("tool-a", config.Tools[0].Name);
        Assert.Equal("src/tool-a/tool-a.csproj", config.Tools[0].Project);

        var tools = config.GetTools();
        Assert.Equal(2, tools.Count);
        Assert.Equal("tool-b", tools[1].Name);
    }

    [Fact]
    public async Task Install_FailsAndStops_OnMissingProject()
    {
        var tools = new List<Tool>
        {
            new() { Project = "does-not-exist.csproj" }
        };

        int result = await BundleInstaller.InstallAsync(
            _tempDir, tools, _tempDir,
            new InstallSource { Type = "local" },
            quiet: true);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public async Task Install_FailsOnEmptyToolset()
    {
        int result = await BundleInstaller.InstallAsync(
            _tempDir, [], _tempDir,
            new InstallSource { Type = "local" },
            quiet: true);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public async Task Install_FailsOnMultiToolEntryWithoutProject()
    {
        // A multi-tool manifest cannot auto-detect; every entry must name a project.
        var tools = new List<Tool>
        {
            new() { Name = "a", Project = "a.csproj" },
            new() { Name = "b", Project = null }
        };

        int result = await BundleInstaller.InstallAsync(
            _tempDir, tools, _tempDir,
            new InstallSource { Type = "local" },
            quiet: true);

        Assert.NotEqual(0, result);
    }
}
