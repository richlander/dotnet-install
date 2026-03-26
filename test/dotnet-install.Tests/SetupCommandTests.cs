namespace dotnet_install.Tests;

/// <summary>
/// Tests for ShellConfig detection and SetupCommand logic.
/// </summary>
public class SetupCommandTests : IDisposable
{
    readonly string _tempDir;
    readonly string _installDir;

    public SetupCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-test-{Path.GetRandomFileName()}");
        _installDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(_installDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // --- ShellConfig tests ---

    [Fact]
    public void IsOnPath_ReturnsTrueWhenDirectoryIsOnPath()
    {
        // PATH always contains /usr/bin or similar
        string pathDir = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "/usr/bin";

        Assert.True(ShellConfig.IsOnPath(pathDir));
    }

    [Fact]
    public void IsOnPath_ReturnsFalseForNonexistentPath()
    {
        Assert.False(ShellConfig.IsOnPath("/some/fake/path/that/does/not/exist"));
    }

    [Fact]
    public void Detect_ReturnsShellName()
    {
        var config = ShellConfig.Detect(_installDir);

        // On CI/macOS/Linux, $SHELL is typically set
        // The shell name should be a reasonable value (or empty if not set)
        Assert.NotNull(config.ShellName);
    }

    [Fact]
    public void Detect_ExportLineContainsPath()
    {
        var config = ShellConfig.Detect(_installDir);

        Assert.Contains("PATH", config.ExportLine);
    }

    [Fact]
    public void Detect_DisplayDirUsesTilde()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dirUnderHome = Path.Combine(home, ".dotnet", "bin");

        var config = ShellConfig.Detect(dirUnderHome);

        Assert.StartsWith("~", config.DisplayDir);
        Assert.Contains(".dotnet/bin", config.DisplayDir);
    }

    [Fact]
    public void RcFileContainsPath_ReturnsFalseWhenFileDoesNotExist()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with
        {
            // Point to a nonexistent file
            RcFileAbsolute = Path.Combine(_tempDir, "nonexistent_rc")
        };

        Assert.False(config.RcFileContainsPath());
    }

    [Fact]
    public void RcFileContainsPath_ReturnsTrueWhenPathAlreadyPresent()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir);

        if (config.RcFileAbsolute is null)
            return; // Skip if shell not detected (e.g., unknown shell on CI)

        // Create a fake rc file with the PATH line
        string fakeRc = Path.Combine(_tempDir, "fake_rc");
        File.WriteAllText(fakeRc, $"# my shell config\n{config.ExportLine}\n");

        config = config with { RcFileAbsolute = fakeRc };

        Assert.True(config.RcFileContainsPath());
    }

    [Fact]
    public void RcFileContainsPath_ReturnsFalseWhenPathNotPresent()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir);

        if (config.RcFileAbsolute is null)
            return;

        string fakeRc = Path.Combine(_tempDir, "fake_rc");
        File.WriteAllText(fakeRc, "# empty config\n");

        config = config with { RcFileAbsolute = fakeRc };

        Assert.False(config.RcFileContainsPath());
    }

    [Fact]
    public void RcLine_UsesExportForBashZsh()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with { ShellName = "bash" };

        // RcLine should source the env file (POSIX dot-source syntax)
        Assert.StartsWith(". \"", config.RcLine);
        Assert.Contains("/env\"", config.RcLine);
    }

    [Fact]
    public void RcLine_UsesFishAddPathForFish()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with { ShellName = "fish" };

        // RcLine should source the fish env file
        Assert.StartsWith("source \"", config.RcLine);
        Assert.Contains("/env.fish\"", config.RcLine);
    }

    [Fact]
    public void EnvFileContent_ContainsExportsForPosix()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with { ShellName = "bash" };

        Assert.Contains("#!/bin/sh", config.EnvFileContent);
        Assert.Contains("export DOTNET_TOOL_BIN=", config.EnvFileContent);
        Assert.Contains("export PATH=", config.EnvFileContent);
    }

    [Fact]
    public void EnvFileContent_UsesFishSyntax()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with
        {
            ShellName = "fish",
            EnvLine = "set -gx DOTNET_TOOL_BIN \"$HOME/.dotnet/bin\"",
            ExportLine = "fish_add_path $DOTNET_TOOL_BIN"
        };

        Assert.DoesNotContain("#!/bin/sh", config.EnvFileContent);
        Assert.Contains("set -gx DOTNET_TOOL_BIN", config.EnvFileContent);
        Assert.Contains("fish_add_path", config.EnvFileContent);
    }

    [Fact]
    public void SourceCommand_UsesDotForPosix()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with { ShellName = "zsh" };

        Assert.StartsWith(". \"", config.SourceCommand);
        Assert.Contains("/env\"", config.SourceCommand);
    }

    [Fact]
    public void SourceCommand_UsesSourceForFish()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with { ShellName = "fish" };

        Assert.StartsWith("source \"", config.SourceCommand);
        Assert.Contains("/env.fish\"", config.SourceCommand);
    }

    [Fact]
    public void Detect_ZshUsesZshenv()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir) with { ShellName = "zsh" };

        // Re-detect with zsh to verify the rc file choice
        // (with override doesn't re-detect, so test via Detect directly if SHELL is zsh)
        if (Path.GetFileName(Environment.GetEnvironmentVariable("SHELL") ?? "") == "zsh")
        {
            var detected = ShellConfig.Detect(dir);
            Assert.Equal("~/.zshenv", detected.RcFile);
        }
    }

    [Fact]
    public void RcFileContainsPath_DetectsEnvFileSourceLine()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".dotnet", "bin");
        var config = ShellConfig.Detect(dir);

        if (config.RcFileAbsolute is null)
            return;

        // Create a fake rc file with the env source line
        string fakeRc = Path.Combine(_tempDir, "fake_rc");
        File.WriteAllText(fakeRc, $"# my shell config\n{config.RcLine}\n");

        config = config with { RcFileAbsolute = fakeRc };

        Assert.True(config.RcFileContainsPath());
    }
}
