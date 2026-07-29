namespace dotnet_install.Tests;

/// <summary>
/// Tests for ToolMetadata sidecar round-trip, legacy fallback, and migration.
/// </summary>
public class ToolMetadataTests : IDisposable
{
    readonly string _tempDir;

    public ToolMetadataTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-test-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void RoundTrip_PreservesSource()
    {
        var original = new ToolManifest
        {
            Source = new InstallSource
            {
                Type = "nuget",
                Package = "mytool",
                Version = "1.2.3"
            }
        };

        ToolMetadata.Write(_tempDir, "mytool", original);
        var loaded = ToolMetadata.Read(_tempDir, "mytool");

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Source);
        Assert.Equal("nuget", loaded.Source.Type);
        Assert.Equal("mytool", loaded.Source.Package);
        Assert.Equal("1.2.3", loaded.Source.Version);
    }

    [Fact]
    public void Read_ReturnNull_WhenFileDoesNotExist()
    {
        string emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = ToolMetadata.Read(emptyDir, "mytool");

        Assert.Null(result);
    }

    [Fact]
    public void Read_ReturnNull_WhenJsonIsCorrupt()
    {
        File.WriteAllText(ToolMetadata.SidecarPath(_tempDir, "mytool"), "not valid json {{{");

        var result = ToolMetadata.Read(_tempDir, "mytool");

        Assert.Null(result);
    }

    [Fact]
    public void Write_CreatesFileAtExpectedPath()
    {
        ToolMetadata.Write(_tempDir, "mytool", new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "x" }
        });

        string expectedPath = Path.Combine(_tempDir, ".tool.mytool.json");
        Assert.True(File.Exists(expectedPath));

        string content = File.ReadAllText(expectedPath);
        Assert.Contains("\"source\"", content);
        Assert.Contains("\"package\"", content);
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        ToolMetadata.Write(_tempDir, "mytool", new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "old" }
        });
        ToolMetadata.Write(_tempDir, "mytool", new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "new" }
        });

        var loaded = ToolMetadata.Read(_tempDir, "mytool");
        Assert.NotNull(loaded);
        Assert.Equal("new", loaded.Source?.Package);
    }

    [Fact]
    public void SidecarPath_IsFlatDotfileNextToBinary()
    {
        string path = ToolMetadata.SidecarPath("/some/dir", "mytool");
        Assert.Equal(Path.Combine("/some/dir", ".tool.mytool.json"), path);
    }

    [Theory]
    [InlineData(".tool.mytool.json", "mytool")]
    [InlineData(".tool.dotnet-inspect.json", "dotnet-inspect")]
    [InlineData(".tool.json", null)]        // legacy sidecar, not a flat one
    [InlineData("mytool", null)]
    [InlineData(".tool.mytool.txt", null)]
    [InlineData("tool.mytool.json", null)]
    public void ToolNameFromSidecar_RecognizesOnlyFlatSidecars(string fileName, string? expected)
    {
        Assert.Equal(expected, ToolMetadata.ToolNameFromSidecar(fileName));
    }

    [Fact]
    public void Read_FallsBackToLegacyDirectory()
    {
        // An install predating the flat sidecar: _mytool/.tool.json
        string legacyDir = Path.Combine(_tempDir, "_mytool");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, ".tool.json"),
            "{\"source\":{\"type\":\"nuget\",\"package\":\"mytool\",\"version\":\"1.0.0\"}}");

        var loaded = ToolMetadata.Read(_tempDir, "mytool");

        Assert.NotNull(loaded);
        Assert.Equal("1.0.0", loaded.Source?.Version);
    }

    [Fact]
    public void Read_PrefersFlatSidecarOverLegacyDirectory()
    {
        string legacyDir = Path.Combine(_tempDir, "_mytool");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, ".tool.json"),
            "{\"source\":{\"type\":\"nuget\",\"package\":\"mytool\",\"version\":\"1.0.0\"}}");
        File.WriteAllText(ToolMetadata.SidecarPath(_tempDir, "mytool"),
            "{\"source\":{\"type\":\"nuget\",\"package\":\"mytool\",\"version\":\"2.0.0\"}}");

        Assert.Equal("2.0.0", ToolMetadata.Read(_tempDir, "mytool")?.Source?.Version);
    }

    [Fact]
    public void Write_MigratesLegacyDirectoryHoldingOnlySidecar()
    {
        string legacyDir = Path.Combine(_tempDir, "_mytool");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, ".tool.json"), "{}");

        ToolMetadata.Write(_tempDir, "mytool", new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "mytool" }
        });

        Assert.False(Directory.Exists(legacyDir));
        Assert.True(File.Exists(ToolMetadata.SidecarPath(_tempDir, "mytool")));
    }

    [Fact]
    public void Write_LeavesLegacyDirectoryHoldingManagedPayload()
    {
        // Payload beyond the sidecar means a legacy managed install; purging it is
        // the install path's job (ResetMetadataDirectory), not a metadata write's.
        string legacyDir = Path.Combine(_tempDir, "_mytool");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, ".tool.json"), "{}");
        File.WriteAllText(Path.Combine(legacyDir, "mytool.dll"), "payload");

        ToolMetadata.Write(_tempDir, "mytool", new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "mytool" }
        });

        Assert.True(Directory.Exists(legacyDir));
    }

    [Fact]
    public void Discover_FindsFlatAndLegacyTools()
    {
        ToolMetadata.Write(_tempDir, "flat-tool", new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "flat-tool" }
        });

        string legacyDir = Path.Combine(_tempDir, "_legacy-tool");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, ".tool.json"),
            "{\"source\":{\"type\":\"nuget\",\"package\":\"legacy-tool\"}}");

        var found = ToolMetadata.Discover(_tempDir);

        Assert.Equal(["flat-tool", "legacy-tool"], found.Select(t => t.Name));
    }

    [Fact]
    public void Delete_RemovesFlatSidecar()
    {
        ToolMetadata.Write(_tempDir, "mytool", new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "mytool" }
        });

        ToolMetadata.Delete(_tempDir, "mytool");

        Assert.False(File.Exists(ToolMetadata.SidecarPath(_tempDir, "mytool")));
        Assert.Null(ToolMetadata.Read(_tempDir, "mytool"));
    }
}
