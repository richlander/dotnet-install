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

    void WriteConfig(string json) =>
        File.WriteAllText(Path.Combine(_tempDir, ".dotnet-install.json"), json);

    [Fact]
    public void Read_ParsesBundleEntries()
    {
        WriteConfig("""
        {
          "bundle": [
            { "project": "src/tool-a/tool-a.csproj" },
            { "project": "src/tool-b/tool-b.csproj" }
          ]
        }
        """);

        var config = ToolConfig.Read(_tempDir);

        Assert.NotNull(config);
        Assert.NotNull(config.Bundle);
        Assert.Equal(2, config.Bundle.Count);
        Assert.Equal("src/tool-a/tool-a.csproj", config.Bundle[0].Project);
        Assert.Equal("src/tool-b/tool-b.csproj", config.Bundle[1].Project);
    }

    [Fact]
    public void Read_BundleIsNull_WhenAbsent()
    {
        WriteConfig("""
        {
          "exe": "solo-tool"
        }
        """);

        var config = ToolConfig.Read(_tempDir);

        Assert.NotNull(config);
        Assert.Null(config.Bundle);
    }

    [Fact]
    public void Read_CoexistsWithUpdateChannel()
    {
        WriteConfig("""
        {
          "update": { "type": "nuget", "package": "solo-tool" },
          "bundle": [ { "project": "app.csproj" } ]
        }
        """);

        var config = ToolConfig.Read(_tempDir);

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
