namespace dotnet_install.Tests;

/// <summary>
/// Tests for the github-release update path's single-file validation (issue #74).
/// The release-asset update channel must enforce the same single-file contract the
/// install path requires, rejecting multi-file or framework-dependent payloads.
/// </summary>
public class ReleasePayloadValidationTests : IDisposable
{
    readonly string _extractDir;

    public ReleasePayloadValidationTests()
    {
        _extractDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-release-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_extractDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_extractDir, true); } catch { }
    }

    static string BinaryName(string baseName) =>
        OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;

    [Fact]
    public void Ok_WhenSingleBinaryPresent()
    {
        string binaryName = BinaryName("mytool");
        File.WriteAllText(Path.Combine(_extractDir, binaryName), "binary");

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.Ok, status);
        Assert.Equal(Path.Combine(_extractDir, binaryName), path);
    }

    [Fact]
    public void NotSingleFile_WhenExtraPayloadPresent()
    {
        string binaryName = BinaryName("mytool");
        File.WriteAllText(Path.Combine(_extractDir, binaryName), "binary");
        // A second significant file (e.g. a framework-dependent dependency).
        File.WriteAllText(Path.Combine(_extractDir, "extra.dll"), "dependency");

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.NotSingleFile, status);
        Assert.Null(path);
    }

    [Fact]
    public void NotSingleFile_WhenNestedPayloadPresent()
    {
        string binaryName = BinaryName("mytool");
        File.WriteAllText(Path.Combine(_extractDir, binaryName), "binary");
        string nested = Path.Combine(_extractDir, "lib");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "dependency.so"), "native dep");

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.NotSingleFile, status);
        Assert.Null(path);
    }

    [Fact]
    public void Ok_WhenSingleBinaryWithDebugSymbols()
    {
        // Debug symbols alongside the binary are ignored, matching IsSingleFile.
        string binaryName = BinaryName("mytool");
        File.WriteAllText(Path.Combine(_extractDir, binaryName), "binary");
        File.WriteAllText(Path.Combine(_extractDir, "mytool.pdb"), "symbols");

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.Ok, status);
        Assert.Equal(Path.Combine(_extractDir, binaryName), path);
    }

    [Fact]
    public void BinaryNotFound_WhenNameMismatch()
    {
        // Single file present, but not the expected binary name.
        File.WriteAllText(Path.Combine(_extractDir, BinaryName("other")), "binary");

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, BinaryName("mytool"), out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.BinaryNotFound, status);
        Assert.Null(path);
    }
}
