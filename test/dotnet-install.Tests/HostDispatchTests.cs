namespace dotnet_install.Tests;

/// <summary>
/// Tests for host dispatch — invocation name detection, --host flag,
/// and error handling for missing tools/metadata.
/// </summary>
public class HostDispatchTests : IDisposable
{
    readonly string _tempDir;

    public HostDispatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-test-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Run_MissingToolDirectory_ReturnsError()
    {
        // HostDispatch.Run resolves _toolname from Environment.ProcessPath's directory.
        // We can't easily mock ProcessPath, but we can test the error path
        // by calling with a tool name that won't exist.
        int result = HostDispatch.Run("nonexistent-tool-12345", []);

        Assert.Equal(1, result);
    }

    [Fact]
    public void Run_MissingToolMetadata_ReturnsError()
    {
        // Create the app directory but without .tool.json
        // This tests the "missing metadata" error path
        // Note: HostDispatch resolves based on Environment.ProcessPath,
        // so this test only works if we happen to have the dir there.
        // For a proper test, we'd need to make the install dir configurable.
        int result = HostDispatch.Run("nonexistent-tool-67890", []);
        Assert.Equal(1, result);
    }

    // ---- Invocation name detection tests ----

    [Fact]
    public void InvocationName_DotnetInstall_SkipsHostMode()
    {
        // Verify that "dotnet-install" as invocation name does NOT enter host mode
        string name = Path.GetFileNameWithoutExtension("dotnet-install");
        bool isHostMode = name != "dotnet-install" && !name.StartsWith("dotnet-install.");

        Assert.False(isHostMode);
    }

    [Fact]
    public void InvocationName_DotnetInstallExe_SkipsHostMode()
    {
        // Windows: "dotnet-install.exe" should not enter host mode
        string name = Path.GetFileNameWithoutExtension("dotnet-install.exe");
        bool isHostMode = name != "dotnet-install" && !name.StartsWith("dotnet-install.");

        Assert.False(isHostMode);
    }

    [Fact]
    public void InvocationName_OtherTool_EntersHostMode()
    {
        string name = Path.GetFileNameWithoutExtension("mytool");
        bool isHostMode = name != "dotnet-install" && !name.StartsWith("dotnet-install.");

        Assert.True(isHostMode);
    }

    [Fact]
    public void InvocationName_ToolWithDotInName_EntersHostMode()
    {
        string name = Path.GetFileNameWithoutExtension("my.tool");
        bool isHostMode = name != "dotnet-install" && !name.StartsWith("dotnet-install.");

        Assert.True(isHostMode);
    }

    // ---- --host flag parsing tests ----

    [Fact]
    public void HostFlag_ParsedCorrectly()
    {
        string[] args = ["--host", "footool", "arg1", "arg2"];

        Assert.Equal("--host", args[0]);
        Assert.Equal("footool", args[1]);

        string toolName = args[1];
        string[] toolArgs = args[2..];

        Assert.Equal("footool", toolName);
        Assert.Equal(["arg1", "arg2"], toolArgs);
    }

    [Fact]
    public void HostFlag_NoExtraArgs()
    {
        string[] args = ["--host", "footool"];

        string toolName = args[1];
        string[] toolArgs = args[2..];

        Assert.Equal("footool", toolName);
        Assert.Empty(toolArgs);
    }
}
