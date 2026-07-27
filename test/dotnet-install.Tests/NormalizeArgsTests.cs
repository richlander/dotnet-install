namespace dotnet_install.Tests;

/// <summary>
/// Tests for <see cref="CommandLineBuilder.NormalizeArgs"/>, which guards a leading
/// positional path against System.CommandLine's invocation-path stripping (a token
/// whose file name matches the command name would otherwise be swallowed, leaving
/// the tool to print help and no-op).
/// </summary>
public class NormalizeArgsTests
{
    [Theory]
    [InlineData("dotnet-install")]
    [InlineData("./dotnet-install")]
    [InlineData("/home/user/git/dotnet-install")]
    [InlineData("../dotnet-install/")]
    [InlineData("DOTNET-INSTALL")]
    [InlineData("dotnet-install.exe")]
    public void SeparatesSoleArg_WhenFileNameMatchesCommand(string arg)
    {
        var result = CommandLineBuilder.NormalizeArgs([arg]);

        Assert.Equal(["--", arg], result);
    }

    [Fact]
    public void MovesMatchingFirstArgToEnd_SoTrailingOptionsBind()
    {
        // The stripping only affects the first token; moving the path past the
        // options keeps them parseable while the path still binds to the operand.
        var result = CommandLineBuilder.NormalizeArgs(["./dotnet-install", "--local-bin"]);

        Assert.Equal(["--local-bin", "./dotnet-install"], result);
    }

    [Fact]
    public void MovesMatchingFirstArgToEnd_PreservingOptionValues()
    {
        var result = CommandLineBuilder.NormalizeArgs(["dotnet-install", "-o", "/tmp/bin"]);

        Assert.Equal(["-o", "/tmp/bin", "dotnet-install"], result);
    }

    [Theory]
    [InlineData("foobar")]
    [InlineData("./src/tool")]
    [InlineData("dotnet-install-extra")]
    [InlineData("install")]
    public void LeavesUnchanged_WhenFirstArgDoesNotMatch(string arg)
    {
        var result = CommandLineBuilder.NormalizeArgs([arg]);

        Assert.Equal([arg], result);
    }

    [Fact]
    public void LeavesUnchanged_WhenFirstArgIsOption()
    {
        var result = CommandLineBuilder.NormalizeArgs(["--repo", "dotnet-install"]);

        Assert.Equal(["--repo", "dotnet-install"], result);
    }

    [Fact]
    public void LeavesUnchanged_WhenEmpty()
    {
        Assert.Empty(CommandLineBuilder.NormalizeArgs([]));
    }

    [Fact]
    public void OnlyGuardsFirstArg()
    {
        // A later positional matching the command name is never stripped, so it
        // needs no guard.
        var result = CommandLineBuilder.NormalizeArgs(["-o", "/tmp/x", "dotnet-install"]);

        Assert.Equal(["-o", "/tmp/x", "dotnet-install"], result);
    }
}
