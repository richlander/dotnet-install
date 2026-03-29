using System.Xml.Linq;

namespace dotnet_install.Tests;

/// <summary>
/// Tests for source build pre-flight TFM checking.
/// Creates mock .csproj files and verifies TargetFramework is parsed correctly.
/// </summary>
public class SourcePreflightTests : IDisposable
{
    readonly string _tempDir;

    public SourcePreflightTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-test-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Theory]
    [InlineData("net8.0", "net8.0")]
    [InlineData("net11.0", "net11.0")]
    [InlineData("net6.0-windows", "net6.0-windows")]
    public void EvaluateProject_ParsesTargetFramework(string tfm, string expected)
    {
        string csproj = CreateCsproj(tfm);
        var info = EvaluateProjectHelper(csproj);

        Assert.Equal(expected, info.targetFramework);
    }

    [Fact]
    public void EvaluateProject_NullTargetFramework_WhenNotSpecified()
    {
        string csproj = CreateCsproj(null);
        var info = EvaluateProjectHelper(csproj);

        Assert.Null(info.targetFramework);
    }

    [Fact]
    public void EvaluateProject_ReadsAssemblyName()
    {
        string path = Path.Combine(_tempDir, "MyApp.csproj");
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <AssemblyName>CustomName</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var info = EvaluateProjectHelper(path);
        Assert.Equal("CustomName", info.assemblyName);
    }

    [Fact]
    public void EvaluateProject_FallsBackToFileName_WhenNoAssemblyName()
    {
        string path = Path.Combine(_tempDir, "MyApp.csproj");
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var info = EvaluateProjectHelper(path);
        Assert.Equal("MyApp", info.assemblyName);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void EvaluateProject_ParsesPublishAot(string? value, bool expected)
    {
        string aotProp = value is not null ? $"<PublishAot>{value}</PublishAot>" : "";
        string path = Path.Combine(_tempDir, $"app-{Path.GetRandomFileName()}.csproj");
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                {aotProp}
              </PropertyGroup>
            </Project>
            """);

        var info = EvaluateProjectHelper(path);
        Assert.Equal(expected, info.isNativeAot);
    }

    // ---- TFM parsing for pre-flight check ----

    [Theory]
    [InlineData("net8.0", 8)]
    [InlineData("net11.0", 11)]
    [InlineData("net6.0-windows", 6)]
    [InlineData("net48", 0)] // .NET Framework — not parseable as "netX.Y"
    public void ParseTfmMajorVersion(string tfm, int expectedMajor)
    {
        int major = ParseMajorFromTfm(tfm);
        Assert.Equal(expectedMajor, major);
    }

    // ---- SDK-implied OutputType tests ----

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Web")]
    [InlineData("Microsoft.NET.Sdk.Worker")]
    public void EvaluateProject_SdkImpliesExe_WhenNoOutputType(string sdk)
    {
        string path = Path.Combine(_tempDir, $"app-{Path.GetRandomFileName()}.csproj");
        File.WriteAllText(path, $"""
            <Project Sdk="{sdk}">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var info = EvaluateProjectHelper(path);
        Assert.Equal("Exe", info.outputType);
    }

    [Fact]
    public void EvaluateProject_StandardSdk_DefaultsToLibrary_WhenNoOutputType()
    {
        string path = Path.Combine(_tempDir, $"lib-{Path.GetRandomFileName()}.csproj");
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var info = EvaluateProjectHelper(path);
        Assert.Equal("Library", info.outputType);
    }

    [Fact]
    public void EvaluateProject_ExplicitOutputType_OverridesSdkDefault()
    {
        string path = Path.Combine(_tempDir, $"app-{Path.GetRandomFileName()}.csproj");
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var info = EvaluateProjectHelper(path);
        Assert.Equal("WinExe", info.outputType);
    }

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Web", true)]
    [InlineData("Microsoft.NET.Sdk.Worker", true)]
    [InlineData("Microsoft.NET.Sdk", false)]
    [InlineData("Microsoft.NET.Sdk.Razor", false)]
    public void SdkImpliesExecutable(string sdk, bool expected)
    {
        Assert.Equal(expected, Installer.SdkImpliesExecutable(sdk));
    }

    [Fact]
    public void SdkImpliesExecutable_Null_ReturnsFalse()
    {
        Assert.False(Installer.SdkImpliesExecutable(null));
    }

    // ---- Helpers ----

    string CreateCsproj(string? tfm)
    {
        string path = Path.Combine(_tempDir, $"test-{Path.GetRandomFileName()}.csproj");
        string tfmProp = tfm is not null ? $"<TargetFramework>{tfm}</TargetFramework>" : "";
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                {tfmProp}
              </PropertyGroup>
            </Project>
            """);
        return path;
    }

    /// <summary>
    /// Mirrors the EvaluateProject logic from Installer.cs for testing.
    /// </summary>
    static (string assemblyName, string outputType, bool isNativeAot, bool isSingleFile, string? targetFramework)
        EvaluateProjectHelper(string projectFile)
    {
        var doc = XDocument.Load(projectFile);
        var props = doc.Descendants()
            .Where(e => e.Parent?.Name.LocalName == "PropertyGroup");

        string? assemblyName = props.FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value;
        if (string.IsNullOrEmpty(assemblyName))
            assemblyName = Path.GetFileNameWithoutExtension(projectFile);

        string? sdk = doc.Root?.Attribute("Sdk")?.Value;
        string defaultOutputType = Installer.SdkImpliesExecutable(sdk) ? "Exe" : "Library";

        string outputType = props.FirstOrDefault(e => e.Name.LocalName == "OutputType")?.Value ?? defaultOutputType;
        bool isNativeAot = string.Equals(
            props.FirstOrDefault(e => e.Name.LocalName == "PublishAot")?.Value,
            "true", StringComparison.OrdinalIgnoreCase);
        bool isSingleFile = string.Equals(
            props.FirstOrDefault(e => e.Name.LocalName == "PublishSingleFile")?.Value,
            "true", StringComparison.OrdinalIgnoreCase);
        string? tfm = props.FirstOrDefault(e => e.Name.LocalName == "TargetFramework")?.Value;

        return (assemblyName, outputType, isNativeAot, isSingleFile, tfm);
    }

    static int ParseMajorFromTfm(string tfm)
    {
        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return 0;

        string versionPart = tfm[3..];
        int dashIndex = versionPart.IndexOf('-');
        if (dashIndex > 0)
            versionPart = versionPart[..dashIndex];

        return Version.TryParse(versionPart, out var v) ? v.Major : 0;
    }
}
