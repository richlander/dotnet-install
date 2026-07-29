namespace dotnet_install.Tests;

/// <summary>
/// Tests for <see cref="BundleInstaller.Resolve"/>, which maps one manifest entry
/// onto something installable. A manifest may mix a repo's own projects with NuGet
/// packages and other repos, so the interesting cases are the source count (zero,
/// one, several) and which kind of entry each source produces.
/// </summary>
public class BundleEntryResolutionTests : IDisposable
{
    readonly string _root;

    public BundleEntryResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dotnet-install-bundle-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>Creates a repo-relative file so project entries resolve.</summary>
    string Touch(string relative)
    {
        string full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
        return full;
    }

    [Fact]
    public void Project_ResolvesToProjectEntry()
    {
        Touch("src/a/a.csproj");
        var tool = new Tool { Name = "a", Project = "src/a/a.csproj" };

        var entry = Assert.IsType<BundleInstaller.ProjectEntry>(
            BundleInstaller.Resolve(tool, _root, toolCount: 2));

        Assert.Equal("a", entry.Name);
        Assert.Equal("src/a/a.csproj", entry.Relative);
        Assert.True(File.Exists(entry.Full));
    }

    [Fact]
    public void Project_FileBasedAppResolves()
    {
        Touch("src/greet.cs");
        var tool = new Tool { Name = "greet", Project = "src/greet.cs" };

        var entry = Assert.IsType<BundleInstaller.ProjectEntry>(
            BundleInstaller.Resolve(tool, _root, toolCount: 2));

        Assert.Equal("src/greet.cs", entry.Relative);
    }

    [Fact]
    public void Project_MissingFileFails()
    {
        var tool = new Tool { Name = "a", Project = "src/nope.csproj" };

        Assert.Null(BundleInstaller.Resolve(tool, _root, toolCount: 2));
    }

    [Fact]
    public void Package_ResolvesToPackageEntry()
    {
        var tool = new Tool { Package = "dotnet-runtimeinfo" };

        var entry = Assert.IsType<BundleInstaller.PackageEntry>(
            BundleInstaller.Resolve(tool, _root, toolCount: 2));

        Assert.Equal("dotnet-runtimeinfo", entry.Spec);
    }

    /// <summary>A pinned package version rides along in the name@version spec.</summary>
    [Fact]
    public void Package_VersionIsAppendedToSpec()
    {
        var tool = new Tool { Package = "dotnet-runtimeinfo", Version = "3.0.1" };

        var entry = Assert.IsType<BundleInstaller.PackageEntry>(
            BundleInstaller.Resolve(tool, _root, toolCount: 2));

        Assert.Equal("dotnet-runtimeinfo@3.0.1", entry.Spec);
    }

    [Fact]
    public void Repository_ResolvesToRepositoryEntry()
    {
        var tool = new Tool { Repository = "richlander/dotnet-inspect" };

        var entry = Assert.IsType<BundleInstaller.RepositoryEntry>(
            BundleInstaller.Resolve(tool, _root, toolCount: 2));

        Assert.Equal("richlander/dotnet-inspect", entry.Spec);
    }

    /// <summary>
    /// A manifest ref may be a branch, tag, or commit, so it goes through the
    /// owner/repo@ref spec that resolves any of the three rather than being
    /// asserted to be one kind.
    /// </summary>
    [Fact]
    public void Repository_RefRidesInTheSpec()
    {
        var tool = new Tool { Repository = "richlander/dotnet-inspect", Ref = "v0.16.0" };

        var entry = Assert.IsType<BundleInstaller.RepositoryEntry>(
            BundleInstaller.Resolve(tool, _root, toolCount: 2));

        Assert.Equal("richlander/dotnet-inspect@v0.16.0", entry.Spec);
    }

    [Fact]
    public void MultipleSourcesFail()
    {
        Touch("src/a/a.csproj");
        var tool = new Tool { Name = "a", Project = "src/a/a.csproj", Package = "dotnetsay" };

        Assert.Null(BundleInstaller.Resolve(tool, _root, toolCount: 1));
    }

    /// <summary>
    /// In a bundle, an entry with no source is ambiguous: nothing says which of the
    /// repo's projects the command maps to.
    /// </summary>
    [Fact]
    public void NoSourceInABundleFails()
    {
        var tool = new Tool { Name = "a" };

        Assert.Null(BundleInstaller.Resolve(tool, _root, toolCount: 2));
    }

    /// <summary>
    /// A one-entry manifest may omit the source and let the repo's sole executable
    /// project be auto-detected.
    /// </summary>
    [Fact]
    public void NoSourceInASingleToolManifestAutoDetects()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "a.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
            </Project>
            """);

        var tool = new Tool { Name = "a" };

        var entry = Assert.IsType<BundleInstaller.ProjectEntry>(
            BundleInstaller.Resolve(tool, _root, toolCount: 1));

        Assert.Equal("a", entry.Name);
        Assert.EndsWith("a.csproj", entry.Full);
    }

    /// <summary>
    /// Whitespace is not a source. The JSON deserializer happily produces empty
    /// strings for <c>"project": ""</c>, which would otherwise resolve to the repo root.
    /// </summary>
    [Fact]
    public void BlankSourceCountsAsNoSource()
    {
        var tool = new Tool { Name = "a", Project = "   " };

        Assert.Null(BundleInstaller.Resolve(tool, _root, toolCount: 2));
    }
}
