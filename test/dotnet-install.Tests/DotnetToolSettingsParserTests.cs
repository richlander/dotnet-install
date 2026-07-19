using DotnetInspector.Services;

namespace dotnet_install.Tests;

// Tests for the DotnetToolSettings.xml parser vendored from richlander/dotnet-inspect.
// Adapted to the local ToolCommand model (Name + EntryPoint + Runner).
public class DotnetToolSettingsParserTests
{
    [Fact]
    public void ParseContent_Version2_IsRidSpecificPointerWithCommandsAndRidPackages()
    {
        const string xml = """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="mytool" EntryPoint="mytool" Runner="" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="MyTool.win-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="MyTool.linux-x64" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """;

        var settings = DotnetToolSettingsParser.ParseContent(xml);

        Assert.NotNull(settings);
        Assert.Equal("2", settings!.Version);
        Assert.True(settings.IsRidSpecificPointerPackage);
        Assert.Collection(settings.Commands!,
            c => { Assert.Equal("mytool", c.Name); Assert.Equal("mytool", c.EntryPoint); Assert.Equal("", c.Runner); });
        Assert.NotNull(settings.RuntimeIdentifierPackages);
        Assert.Collection(settings.RuntimeIdentifierPackages!,
            r => { Assert.Equal("win-x64", r.RuntimeIdentifier); Assert.Equal("MyTool.win-x64", r.PackageId); },
            r => { Assert.Equal("linux-x64", r.RuntimeIdentifier); Assert.Equal("MyTool.linux-x64", r.PackageId); });
    }

    [Fact]
    public void ParseContent_Version1_IsPortableWithNoRidPackages()
    {
        const string xml = """
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="portabletool" EntryPoint="portabletool.dll" Runner="dotnet" />
              </Commands>
            </DotNetCliTool>
            """;

        var settings = DotnetToolSettingsParser.ParseContent(xml);

        Assert.NotNull(settings);
        Assert.Equal("1", settings!.Version);
        Assert.False(settings.IsRidSpecificPointerPackage);
        Assert.Collection(settings.Commands!,
            c => { Assert.Equal("portabletool", c.Name); Assert.Equal("portabletool.dll", c.EntryPoint); Assert.Equal("dotnet", c.Runner); });
        Assert.Null(settings.RuntimeIdentifierPackages);
    }

    [Fact]
    public void ParseContent_NoVersionAttribute_TreatedAsPortable()
    {
        const string xml = """
            <DotNetCliTool>
              <Commands>
                <Command Name="legacy" />
              </Commands>
            </DotNetCliTool>
            """;

        var settings = DotnetToolSettingsParser.ParseContent(xml);

        Assert.NotNull(settings);
        Assert.Null(settings!.Version);
        Assert.False(settings.IsRidSpecificPointerPackage);
        Assert.Collection(settings.Commands!,
            c => { Assert.Equal("legacy", c.Name); Assert.Null(c.EntryPoint); Assert.Null(c.Runner); });
    }

    [Fact]
    public void ParseContent_UnrecognizedVersion_ReturnsNull()
    {
        Assert.Null(DotnetToolSettingsParser.ParseContent("""<DotNetCliTool Version="99" />"""));
    }

    [Fact]
    public void ParseContent_MalformedXml_ReturnsNullWithoutThrowing()
    {
        Assert.Null(DotnetToolSettingsParser.ParseContent("<DotNetCliTool Version=\"2\"><Commands>"));
    }

    [Fact]
    public void ParseContent_DtdIsProhibited_ReturnsNullWithoutExpanding()
    {
        // HardenedXml disables DTD processing; a manifest carrying a DOCTYPE must not parse.
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE DotNetCliTool [ <!ENTITY x "boom"> ]>
            <DotNetCliTool Version="1"><Commands><Command Name="&x;" /></Commands></DotNetCliTool>
            """;

        Assert.Null(DotnetToolSettingsParser.ParseContent(xml));
    }

    [Fact]
    public void FindSettings_LocatesFileAtRootOneAndTwoLevelsDeep()
    {
        var root = NewTempDir();
        try
        {
            var atRoot = Path.Combine(root, "DotnetToolSettings.xml");
            File.WriteAllText(atRoot, "<DotNetCliTool Version=\"1\" />");
            Assert.Equal(atRoot, DotnetToolSettingsParser.FindSettings(root));

            var root1 = NewTempDir();
            var tfmDir = Path.Combine(root1, "net8.0");
            Directory.CreateDirectory(tfmDir);
            var atLevel1 = Path.Combine(tfmDir, "DotnetToolSettings.xml");
            File.WriteAllText(atLevel1, "<DotNetCliTool Version=\"1\" />");
            Assert.Equal(atLevel1, DotnetToolSettingsParser.FindSettings(root1));

            var root2 = NewTempDir();
            var ridDir = Path.Combine(root2, "net8.0", "any");
            Directory.CreateDirectory(ridDir);
            var atLevel2 = Path.Combine(ridDir, "DotnetToolSettings.xml");
            File.WriteAllText(atLevel2, "<DotNetCliTool Version=\"1\" />");
            Assert.Equal(atLevel2, DotnetToolSettingsParser.FindSettings(root2));

            Directory.Delete(root1, recursive: true);
            Directory.Delete(root2, recursive: true);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindAndParse_EndToEnd_ReturnsParsedSettings()
    {
        var root = NewTempDir();
        var tfmDir = Path.Combine(root, "net8.0");
        Directory.CreateDirectory(tfmDir);
        File.WriteAllText(Path.Combine(tfmDir, "DotnetToolSettings.xml"), """
            <DotNetCliTool Version="2">
              <Commands><Command Name="mytool" /></Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="MyTool.win-x64" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """);
        try
        {
            var settings = DotnetToolSettingsParser.FindAndParse(root);

            Assert.NotNull(settings);
            Assert.True(settings!.IsRidSpecificPointerPackage);
            Assert.Equal("mytool", Assert.Single(settings.Commands!).Name);
            Assert.Single(settings.RuntimeIdentifierPackages!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindAndParse_NoManifest_ReturnsNull()
    {
        var root = NewTempDir();
        try
        {
            Assert.Null(DotnetToolSettingsParser.FindAndParse(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tool-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
