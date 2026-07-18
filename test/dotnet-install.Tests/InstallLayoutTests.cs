namespace dotnet_install.Tests;

/// <summary>
/// Tests for legacy managed-install detection and cleanup (issue #75). Pre-redesign
/// managed tools installed a symlink/shim backed by a _&lt;name&gt;/ directory of DLLs;
/// after the single-file redesign those must be labeled honestly and cleaned up.
/// </summary>
public class InstallLayoutTests : IDisposable
{
    readonly string _installDir;

    public InstallLayoutTests()
    {
        _installDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-layout-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_installDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_installDir, true); } catch { }
    }

    // A current single-file install: a real binary plus _<name>/ holding only metadata.
    FileInfo WriteSingleFileInstall(string name)
    {
        string binary = Path.Combine(_installDir, name);
        File.WriteAllBytes(binary, [0x7F, (byte)'E', (byte)'L', (byte)'F']);
        string appDir = Path.Combine(_installDir, $"_{name}");
        Directory.CreateDirectory(appDir);
        ToolMetadata.Write(appDir, new ToolManifest { Source = new InstallSource { Type = "nuget", Package = name } });
        return new FileInfo(binary);
    }

    [Fact]
    public void SingleFileInstall_IsNotLegacy()
    {
        FileInfo entry = WriteSingleFileInstall("mytool");
        Assert.False(InstallLayout.IsLegacyManaged(_installDir, "mytool", entry));
        Assert.Equal(InstallLayout.SingleFileType, InstallLayout.ClassifyType(_installDir, "mytool", entry));
    }

    [Fact]
    public void LegacyPayloadInAppDir_IsLegacy()
    {
        FileInfo entry = WriteSingleFileInstall("mytool");
        // A leftover managed DLL alongside the .tool.json marks the legacy layout.
        File.WriteAllBytes(Path.Combine(_installDir, "_mytool", "mytool.dll"), [0x4D, 0x5A]);

        Assert.True(InstallLayout.IsLegacyManaged(_installDir, "mytool", entry));
        Assert.Equal(InstallLayout.LegacyManagedType, InstallLayout.ClassifyType(_installDir, "mytool", entry));
    }

    [Fact]
    public void SymlinkEntry_IsLegacy()
    {
        if (OperatingSystem.IsWindows())
            return;

        string link = Path.Combine(_installDir, "mytool");
        File.CreateSymbolicLink(link, Path.Combine(_installDir, "_mytool", "mytool.dll"));

        Assert.True(InstallLayout.IsLegacyManaged(_installDir, "mytool", new FileInfo(link)));
    }

    [Fact]
    public void EmptyAppDir_IsNotLegacy()
    {
        FileInfo entry = WriteSingleFileInstall("mytool");
        // Remove the metadata so _<name>/ is empty: still not "managed payload".
        File.Delete(Path.Combine(_installDir, "_mytool", ToolMetadata.FileName));

        Assert.False(InstallLayout.IsLegacyManaged(_installDir, "mytool", entry));
    }

    [Fact]
    public void Remove_CleansDanglingLegacySymlinkAndPayload()
    {
        if (OperatingSystem.IsWindows())
            return;

        string appDir = Path.Combine(_installDir, "_mytool");
        Directory.CreateDirectory(appDir);
        File.WriteAllBytes(Path.Combine(appDir, "mytool.dll"), [0x4D, 0x5A]);

        // A legacy launcher whose target is already gone (dangling symlink).
        string link = Path.Combine(_installDir, "mytool");
        File.CreateSymbolicLink(link, Path.Combine(appDir, "missing-launcher"));

        int rc = RemoveCommand.Run(_installDir, ["mytool"]);

        Assert.Equal(0, rc);
        Assert.False(new FileInfo(link).Exists || new FileInfo(link).LinkTarget is not null);
        Assert.False(Directory.Exists(appDir));
    }
}
