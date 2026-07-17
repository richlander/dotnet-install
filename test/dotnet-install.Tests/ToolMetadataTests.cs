namespace dotnet_install.Tests;

/// <summary>
/// Tests for ToolMetadata .tool.json round-trip serialization.
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

        ToolMetadata.Write(_tempDir, original);
        var loaded = ToolMetadata.Read(_tempDir);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Source);
        Assert.Equal("nuget", loaded.Source.Type);
        Assert.Equal("mytool", loaded.Source.Package);
        Assert.Equal("1.2.3", loaded.Source.Version);
    }

    [Fact]
    public void RoundTrip_PreservesUpdateChannel()
    {
        var original = new ToolManifest
        {
            Source = new InstallSource { Type = "github", Repository = "owner/repo" },
            Update = new InstallSource { Type = "nuget", Package = "mytool", Version = "2.0.0" }
        };

        ToolMetadata.Write(_tempDir, original);
        var loaded = ToolMetadata.Read(_tempDir);

        Assert.NotNull(loaded);
        Assert.Equal("github", loaded.Source?.Type);
        Assert.Equal("nuget", loaded.Update?.Type);
        Assert.Equal("2.0.0", loaded.Update?.Version);
    }

    [Fact]
    public void Read_ReturnNull_WhenFileDoesNotExist()
    {
        string emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = ToolMetadata.Read(emptyDir);

        Assert.Null(result);
    }

    [Fact]
    public void Read_ReturnNull_WhenJsonIsCorrupt()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".tool.json"), "not valid json {{{");

        var result = ToolMetadata.Read(_tempDir);

        Assert.Null(result);
    }

    [Fact]
    public void Write_CreatesFileAtExpectedPath()
    {
        ToolMetadata.Write(_tempDir, new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "x" }
        });

        string expectedPath = Path.Combine(_tempDir, ".tool.json");
        Assert.True(File.Exists(expectedPath));

        string content = File.ReadAllText(expectedPath);
        Assert.Contains("\"source\"", content);
        Assert.Contains("\"package\"", content);
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        ToolMetadata.Write(_tempDir, new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "old" }
        });
        ToolMetadata.Write(_tempDir, new ToolManifest
        {
            Source = new InstallSource { Type = "nuget", Package = "new" }
        });

        var loaded = ToolMetadata.Read(_tempDir);
        Assert.NotNull(loaded);
        Assert.Equal("new", loaded.Source?.Package);
    }

    [Fact]
    public void GetPath_ReturnsCorrectLocation()
    {
        string path = ToolMetadata.GetPath("/some/dir");
        Assert.Equal(Path.Combine("/some/dir", ".tool.json"), path);
    }
}
