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
    public void RoundTrip_PreservesAllFields()
    {
        var original = new ToolManifest
        {
            EntryPoint = "mytool.dll",
            RollForward = true
        };

        ToolMetadata.Write(_tempDir, original);
        var loaded = ToolMetadata.Read(_tempDir);

        Assert.NotNull(loaded);
        Assert.Equal("mytool.dll", loaded.EntryPoint);
        Assert.True(loaded.RollForward);
    }

    [Fact]
    public void RoundTrip_DefaultRollForwardIsFalse()
    {
        var original = new ToolManifest { EntryPoint = "app.dll" };

        ToolMetadata.Write(_tempDir, original);
        var loaded = ToolMetadata.Read(_tempDir);

        Assert.NotNull(loaded);
        Assert.Equal("app.dll", loaded.EntryPoint);
        Assert.False(loaded.RollForward);
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
        ToolMetadata.Write(_tempDir, new ToolManifest { EntryPoint = "x.dll" });

        string expectedPath = Path.Combine(_tempDir, ".tool.json");
        Assert.True(File.Exists(expectedPath));

        string content = File.ReadAllText(expectedPath);
        Assert.Contains("\"entryPoint\"", content);
        Assert.Contains("x.dll", content);
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        ToolMetadata.Write(_tempDir, new ToolManifest { EntryPoint = "old.dll" });
        ToolMetadata.Write(_tempDir, new ToolManifest { EntryPoint = "new.dll" });

        var loaded = ToolMetadata.Read(_tempDir);
        Assert.NotNull(loaded);
        Assert.Equal("new.dll", loaded.EntryPoint);
    }

    [Fact]
    public void GetPath_ReturnsCorrectLocation()
    {
        string path = ToolMetadata.GetPath("/some/dir");
        Assert.Equal(Path.Combine("/some/dir", ".tool.json"), path);
    }
}
