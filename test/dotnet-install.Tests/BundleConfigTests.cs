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
        { "exe": "solo-tool", "update": { "type": "nuget", "package": "solo-tool" } }
        """);

        var config = ToolConfig.Read(_tempDir);

        Assert.NotNull(config);
        Assert.Equal("solo-tool", config.Exe);
        Assert.Equal("nuget", config.Update?.Type);
    }

    [Fact]
    public void ReadFromRepo_ReturnsNull_WhenAbsent()
    {
        Assert.Null(ToolConfig.ReadFromRepo(_tempDir));
    }

    [Fact]
    public void ReadFromRepo_CoexistsWithUpdateChannel()
    {
        WriteRepoManifest("""
        {
          "update": { "type": "nuget", "package": "solo-tool" },
          "bundle": [ { "project": "app.csproj" } ]
        }
        """);

        var config = ToolConfig.ReadFromRepo(_tempDir);

        Assert.NotNull(config);
        Assert.Equal("nuget", config.Update?.Type);
        Assert.Single(config.Bundle!);
        Assert.Equal("app.csproj", config.Bundle![0].Project);
    }

    [Fact]
    public void Install_FailsAndStops_OnMissingProject()
    {
        var bundle = new List<BundleEntry>
        {
            new() { Project = "does-not-exist.csproj" }
        };

        int result = BundleInstaller.Install(
            _tempDir, bundle, _tempDir,
            new InstallSource { Type = "local" },
            quiet: true);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Install_FailsOnEmptyBundle()
    {
        int result = BundleInstaller.Install(
            _tempDir, [], _tempDir,
            new InstallSource { Type = "local" },
            quiet: true);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public void Install_FailsOnEntryWithoutProject()
    {
        var bundle = new List<BundleEntry> { new() { Project = null } };

        int result = BundleInstaller.Install(
            _tempDir, bundle, _tempDir,
            new InstallSource { Type = "local" },
            quiet: true);

        Assert.NotEqual(0, result);
    }
}
