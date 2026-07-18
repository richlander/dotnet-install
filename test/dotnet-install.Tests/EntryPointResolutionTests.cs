using System.Runtime.InteropServices;

namespace dotnet_install.Tests;

/// <summary>
/// Tests for resolving a NuGet tool's executable via the EntryPoint declared in
/// DotnetToolSettings.xml. The command name the user types can differ from the
/// packaged executable file name, so resolving by command name alone would
/// falsely reject otherwise-valid CLI tools v2 packages (issue #76).
/// </summary>
public class EntryPointResolutionTests : IDisposable
{
    readonly string _tempDir;

    public EntryPointResolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-entry-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    static string ExeName(string baseName) =>
        OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;

    [Fact]
    public void ResolvesByEntryPoint_WhenCommandNameDiffers()
    {
        // Command the user types ("mytool") differs from the packaged file ("tool-impl").
        string entryFile = ExeName("tool-impl");
        File.WriteAllText(Path.Combine(_tempDir, entryFile), "binary");

        var info = new Installer.ToolSettings("mytool", entryFile, "executable", _tempDir);
        var resolved = Installer.ResolveEntryExecutable(_tempDir, info);

        Assert.Equal(Path.Combine(_tempDir, entryFile), resolved);
    }

    [Fact]
    public void FallsBackToCommandName_WhenEntryPointMissing()
    {
        string commandFile = ExeName("mytool");
        File.WriteAllText(Path.Combine(_tempDir, commandFile), "binary");

        var info = new Installer.ToolSettings("mytool", EntryPoint: "", "executable", _tempDir);
        var resolved = Installer.ResolveEntryExecutable(_tempDir, info);

        Assert.Equal(Path.Combine(_tempDir, commandFile), resolved);
    }

    [Fact]
    public void ResolvesEntryPointWithoutExtension_OnWindows()
    {
        // Some packages declare EntryPoint without the platform extension.
        if (!OperatingSystem.IsWindows())
            return;

        File.WriteAllText(Path.Combine(_tempDir, "mytool.exe"), "binary");

        var info = new Installer.ToolSettings("mytool", EntryPoint: "mytool", "executable", _tempDir);
        var resolved = Installer.ResolveEntryExecutable(_tempDir, info);

        Assert.Equal(Path.Combine(_tempDir, "mytool.exe"), resolved);
    }

    [Fact]
    public void ReturnsNull_WhenNoExecutableExists()
    {
        var info = new Installer.ToolSettings("mytool", "missing.exe", "executable", _tempDir);
        var resolved = Installer.ResolveEntryExecutable(_tempDir, info);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolvesManagedDllEntryPoint()
    {
        // A managed tool declares a .dll EntryPoint; resolution still finds the file so
        // the caller can classify it as managed (Runner="dotnet") and reject it.
        File.WriteAllText(Path.Combine(_tempDir, "mytool.dll"), "assembly");

        var info = new Installer.ToolSettings("mytool", "mytool.dll", "dotnet", _tempDir);
        var resolved = Installer.ResolveEntryExecutable(_tempDir, info);

        Assert.Equal(Path.Combine(_tempDir, "mytool.dll"), resolved);
    }
}
