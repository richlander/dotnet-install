namespace dotnet_install.Tests;

/// <summary>
/// Tests for <see cref="Installer.IsSingleFile"/>: a publish output counts as
/// single-file only when it contains exactly one significant file. Debug symbols
/// (.pdb/.dbg, macOS .dSYM bundles) and tool metadata are ignored; nested payloads
/// are not.
/// </summary>
public class SingleFileDetectionTests : IDisposable
{
    readonly string _dir;

    public SingleFileDetectionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"dotnet-install-sf-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    void Touch(string relativePath)
    {
        string full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    [Fact]
    public void LoneExecutable_IsSingleFile()
    {
        Touch("app");
        Assert.True(Installer.IsSingleFile(_dir));
    }

    [Fact]
    public void ExecutableWithDebugSymbols_IsSingleFile()
    {
        Touch("app");
        Touch("app.pdb");
        Touch("app.dbg");
        Assert.True(Installer.IsSingleFile(_dir));
    }

    [Fact]
    public void ExecutableWithToolSettings_IsSingleFile()
    {
        Touch("app");
        Touch("DotnetToolSettings.xml");
        Assert.True(Installer.IsSingleFile(_dir));
    }

    [Fact]
    public void ExecutableWithMacDsymBundle_IsSingleFile()
    {
        // macOS Native AOT emits a app.dSYM/ debug-symbols bundle beside the binary.
        Touch("app");
        Touch("app.dSYM/Contents/Info.plist");
        Touch("app.dSYM/Contents/Resources/DWARF/app");
        Assert.True(Installer.IsSingleFile(_dir));
    }

    [Fact]
    public void ExecutableWithNestedPayload_IsNotSingleFile()
    {
        Touch("app");
        Touch("lib/dependency.so");
        Assert.False(Installer.IsSingleFile(_dir));
    }

    [Fact]
    public void ExecutableWithSiblingManagedFile_IsNotSingleFile()
    {
        Touch("app");
        Touch("app.dll");
        Assert.False(Installer.IsSingleFile(_dir));
    }
}
