namespace dotnet_install.Tests;

// Tests for detecting a conflicting same-named executable already on PATH
// (including .NET SDK global-tool shims), by resolving the executable rather
// than parsing `dotnet tool list` output.
public class GlobalToolCheckTests
{
    const string InstallDir = "/home/user/.dotnet/bin";
    const string ToolsDir = "/home/user/.dotnet/tools";
    const string OtherDir = "/usr/local/bin";

    static readonly string[] Command = new[] { "dotnet-inspect" };

    static GlobalToolCheck.Conflict? Resolve(IReadOnlyList<string> dirs, Func<string, bool> exists) =>
        GlobalToolCheck.Resolve(dirs, InstallDir, ToolsDir, Command, exists);

    [Fact]
    public void Resolve_FlagsSdkGlobalTool_WhenShimExists()
    {
        var conflict = Resolve(
            new[] { InstallDir, ToolsDir, OtherDir },
            p => p == "/home/user/.dotnet/tools/dotnet-inspect");

        Assert.NotNull(conflict);
        Assert.True(conflict!.IsSdkGlobalTool);
        Assert.Equal("/home/user/.dotnet/tools/dotnet-inspect", conflict.Path);
    }

    [Fact]
    public void Resolve_FlagsNonSdkConflict_ForOtherPathDir()
    {
        var conflict = Resolve(
            new[] { InstallDir, ToolsDir, OtherDir },
            p => p == "/usr/local/bin/dotnet-inspect");

        Assert.NotNull(conflict);
        Assert.False(conflict!.IsSdkGlobalTool);
        Assert.Equal("/usr/local/bin/dotnet-inspect", conflict.Path);
    }

    [Fact]
    public void Resolve_IgnoresOurOwnInstallDir()
    {
        // The command already living in our target dir is not a conflict.
        var conflict = Resolve(
            new[] { InstallDir, ToolsDir, OtherDir },
            p => p == "/home/user/.dotnet/bin/dotnet-inspect");

        Assert.Null(conflict);
    }

    [Fact]
    public void Resolve_PrefersRealConflict_OverInstallDirCopy()
    {
        // Present in both our install dir and the global-tools dir: install dir is
        // skipped, so the SDK tool is reported.
        var conflict = Resolve(
            new[] { InstallDir, ToolsDir },
            p => p == "/home/user/.dotnet/bin/dotnet-inspect"
              || p == "/home/user/.dotnet/tools/dotnet-inspect");

        Assert.NotNull(conflict);
        Assert.True(conflict!.IsSdkGlobalTool);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenCommandNotFound()
    {
        var conflict = Resolve(new[] { InstallDir, ToolsDir, OtherDir }, _ => false);
        Assert.Null(conflict);
    }

    [Fact]
    public void ExecutableNames_IncludesCommandForCurrentOs()
    {
        var names = GlobalToolCheck.ExecutableNames("dotnet-inspect");
        Assert.Contains("dotnet-inspect", (IEnumerable<string>)names);
    }
}
