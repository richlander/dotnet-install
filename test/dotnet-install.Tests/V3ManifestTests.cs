using System.Text.Json;

namespace dotnet_install.Tests;

/// <summary>
/// Tests for the DotNetCliTool v3 package manifest (tools/manifest.json):
/// parsing, RID selection, and bundle version parsing.
/// </summary>
public class V3ManifestTests : IDisposable
{
    readonly string _tempDir;

    public V3ManifestTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-v3-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    void WriteManifest(string json)
    {
        string dir = Path.Combine(_tempDir, "tools");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), json);
    }

    [Fact]
    public void TryRead_ReturnsNull_WhenNoManifest()
    {
        Assert.Null(V3Manifest.TryRead(_tempDir));
    }

    [Fact]
    public void TryRead_ParsesIndexPointer()
    {
        WriteManifest("""
        {
          "version": 3,
          "name": "mytool",
          "index": [
            { "rid": "linux-x64", "id": "mytool.linux-x64" },
            { "rid": "win-x64", "id": "mytool.win-x64" },
            { "rid": "any", "id": "mytool.any", "runtimeVersion": "9.0" }
          ]
        }
        """);

        var m = V3Manifest.TryRead(_tempDir);

        Assert.NotNull(m);
        Assert.Equal(3, m.Version);
        Assert.Equal("mytool", m.Name);
        Assert.Equal(3, m.Index!.Count);
        Assert.Equal("mytool.linux-x64", m.Index[0].Id);
        Assert.Equal("9.0", m.Index[2].RuntimeVersion);
    }

    [Fact]
    public void TryRead_ParsesBundlePointer()
    {
        WriteManifest("""
        {
          "version": 3,
          "bundle": [
            { "id": "tool.a", "version": "[9.0.661903]" },
            { "id": "tool.b" }
          ]
        }
        """);

        var m = V3Manifest.TryRead(_tempDir);

        Assert.NotNull(m);
        Assert.Equal(2, m.Bundle!.Count);
        Assert.Equal("tool.a", m.Bundle[0].Id);
        Assert.Equal("[9.0.661903]", m.Bundle[0].Version);
        Assert.Null(m.Bundle[1].Version);
    }

    [Fact]
    public void TryRead_ParsesRidSpecificPayload()
    {
        WriteManifest("""
        {
          "version": 3,
          "descriptor": { "rid": "linux-x64", "id": "mytool.linux-x64" },
          "commands": [
            { "entryPoint": "mytool" }
          ]
        }
        """);

        var m = V3Manifest.TryRead(_tempDir);

        Assert.NotNull(m);
        Assert.Equal("linux-x64", m.Descriptor!.Rid);
        Assert.Single(m.Commands!);
        Assert.Equal("mytool", m.Commands![0].EntryPoint);
        Assert.Null(m.Commands[0].Runner);
    }

    [Fact]
    public void TryRead_ReturnsNull_OnInvalidJson()
    {
        WriteManifest("{ not valid json");
        Assert.Null(V3Manifest.TryRead(_tempDir));
    }

    [Fact]
    public void SelectRid_PrefersExactRid_OverAny()
    {
        var index = new List<V3RidRef>
        {
            new() { Rid = "any", Id = "mytool.any" },
            new() { Rid = "linux-x64", Id = "mytool.linux-x64" },
        };

        var selected = V3Manifest.SelectRid(index, new[] { "linux-x64", "linux", "unix", "any" });

        Assert.Equal("mytool.linux-x64", selected!.Id);
    }

    [Fact]
    public void SelectRid_FallsBackToAny()
    {
        var index = new List<V3RidRef>
        {
            new() { Rid = "any", Id = "mytool.any" },
        };

        var selected = V3Manifest.SelectRid(index, new[] { "linux-x64", "linux", "unix", "any" });

        Assert.Equal("mytool.any", selected!.Id);
    }

    [Fact]
    public void SelectRid_ReturnsNull_WhenNoMatch()
    {
        var index = new List<V3RidRef>
        {
            new() { Rid = "win-x64", Id = "mytool.win-x64" },
        };

        var selected = V3Manifest.SelectRid(index, new[] { "linux-x64", "linux", "unix", "any" });

        Assert.Null(selected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("9.0.1", "9.0.1")]
    [InlineData("[9.0.661903]", "9.0.661903")]
    [InlineData("[9.0.1, 10.0.0)", null)]
    public void ParseExactVersion_HandlesRangesAndBareVersions(string? input, string? expected)
    {
        Assert.Equal(expected, Installer.ParseExactVersion(input));
    }
}
