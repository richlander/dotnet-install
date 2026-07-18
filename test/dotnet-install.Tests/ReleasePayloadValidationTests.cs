using System.Formats.Tar;
using System.IO.Compression;

namespace dotnet_install.Tests;

/// <summary>
/// Tests for the github-release update path's single-file validation (issue #74)
/// and safe archive extraction (zip-slip / tar-slip protection).
/// The release-asset update channel must enforce the same single-file contract the
/// install path requires, rejecting multi-file or framework-dependent payloads.
/// </summary>
public class ReleasePayloadValidationTests : IDisposable
{
    readonly string _extractDir;
    readonly string _workDir;

    public ReleasePayloadValidationTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-release-{Path.GetRandomFileName()}");
        _extractDir = Path.Combine(_workDir, "extract");
        Directory.CreateDirectory(_extractDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, true); } catch { }
    }

    static string BinaryName(string baseName) =>
        OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;

    // A minimal but realistic native-executable header so tests don't rely on
    // plain-text stand-ins for the binary payload.
    static readonly byte[] ElfHeader = [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0];

    [Fact]
    public void Ok_WhenSingleBinaryPresent()
    {
        string binaryName = BinaryName("mytool");
        File.WriteAllBytes(Path.Combine(_extractDir, binaryName), ElfHeader);

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.Ok, status);
        Assert.Equal(Path.Combine(_extractDir, binaryName), path);
    }

    [Fact]
    public void NotSingleFile_WhenExtraPayloadPresent()
    {
        string binaryName = BinaryName("mytool");
        File.WriteAllBytes(Path.Combine(_extractDir, binaryName), ElfHeader);
        // A second significant file (e.g. a framework-dependent dependency).
        File.WriteAllBytes(Path.Combine(_extractDir, "extra.dll"), ElfHeader);

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.NotSingleFile, status);
        Assert.Null(path);
    }

    [Fact]
    public void NotSingleFile_WhenNestedPayloadPresent()
    {
        string binaryName = BinaryName("mytool");
        File.WriteAllBytes(Path.Combine(_extractDir, binaryName), ElfHeader);
        string nested = Path.Combine(_extractDir, "lib");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "dependency.so"), ElfHeader);

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.NotSingleFile, status);
        Assert.Null(path);
    }

    [Fact]
    public void Ok_WhenSingleBinaryWithDebugSymbols()
    {
        // Debug symbols alongside the binary are ignored, matching IsSingleFile.
        string binaryName = BinaryName("mytool");
        File.WriteAllBytes(Path.Combine(_extractDir, binaryName), ElfHeader);
        File.WriteAllText(Path.Combine(_extractDir, "mytool.pdb"), "symbols");

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, binaryName, out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.Ok, status);
        Assert.Equal(Path.Combine(_extractDir, binaryName), path);
    }

    [Fact]
    public void BinaryNotFound_WhenNameMismatch()
    {
        // Single file present, but not the expected binary name.
        File.WriteAllBytes(Path.Combine(_extractDir, BinaryName("other")), ElfHeader);

        var status = UpdateCommand.ValidateReleasePayload(_extractDir, BinaryName("mytool"), out string? path);

        Assert.Equal(UpdateCommand.ReleasePayloadStatus.BinaryNotFound, status);
        Assert.Null(path);
    }

    [Fact]
    public void Extraction_RejectsTarSlipEntry()
    {
        // A malicious tar.gz whose entry escapes the destination directory must not
        // write outside extractDir. (Unix extraction path uses TarFile.)
        if (OperatingSystem.IsWindows())
            return;

        string archivePath = Path.Combine(_workDir, "evil.tar.gz");
        string escapeMarker = Path.Combine(_workDir, "escaped.txt");

        using (var fs = File.Create(archivePath))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        using (var tar = new TarWriter(gz))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escaped.txt")
            {
                DataStream = new MemoryStream(ElfHeader),
            };
            tar.WriteEntry(entry);
        }

        bool ok = UpdateCommand.TryExtractReleaseArchive(archivePath, _extractDir, isWindows: false);

        Assert.False(ok);
        Assert.False(File.Exists(escapeMarker));
    }

    [Fact]
    public void Extraction_UnpacksLegitimateTarball()
    {
        if (OperatingSystem.IsWindows())
            return;

        string archivePath = Path.Combine(_workDir, "good.tar.gz");
        string binaryName = BinaryName("mytool");

        using (var fs = File.Create(archivePath))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        using (var tar = new TarWriter(gz))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, binaryName)
            {
                DataStream = new MemoryStream(ElfHeader),
            };
            tar.WriteEntry(entry);
        }

        bool ok = UpdateCommand.TryExtractReleaseArchive(archivePath, _extractDir, isWindows: false);

        Assert.True(ok);
        Assert.True(File.Exists(Path.Combine(_extractDir, binaryName)));
    }
}
