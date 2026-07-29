namespace dotnet_install.Tests;

/// <summary>
/// Tests the checked-in <c>composed-repo</c> fixture, a manifest that names every
/// entry kind at once: two of the repo's own projects, another repo, and a NuGet
/// package. <see cref="BundleEntryResolutionTests"/> covers each kind in isolation
/// with synthetic input; this covers them composed, against the real file a repo
/// would commit.
///
/// Offline by design — entries are resolved, not installed. Nothing here clones,
/// downloads, or builds. See the fixture's README for the end-to-end run.
/// </summary>
public class ComposedRepoFixtureTests
{
    static readonly string FixtureRoot = FindFixture();

    /// <summary>
    /// Walks up from the test assembly to the repo root. The assembly lives under
    /// <c>artifacts/bin/...</c>, whose depth varies by configuration and TFM, so
    /// the fixture is found by searching rather than by a relative path.
    /// </summary>
    static string FindFixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "test", "fixtures", "composed-repo");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"composed-repo fixture not found above {AppContext.BaseDirectory}");
    }

    static List<Tool> ReadTools()
    {
        var config = ToolConfig.ReadFromRepo(FixtureRoot);
        Assert.NotNull(config);
        return config!.GetTools();
    }

    [Fact]
    public void Manifest_IsReadable()
    {
        var config = ToolConfig.ReadFromRepo(FixtureRoot);

        Assert.NotNull(config);
        Assert.Equal(3, config!.Version);
        Assert.Equal("composed-repo", config.Name);
    }

    [Fact]
    public void Manifest_DeclaresFourTools()
    {
        Assert.Equal(4, ReadTools().Count);
    }

    /// <summary>
    /// Every entry must name exactly one source. A second source is the ambiguity
    /// the resolver rejects, so the fixture would be testing error handling
    /// instead of composition if this ever regressed.
    /// </summary>
    [Fact]
    public void EveryEntry_DeclaresExactlyOneSource()
    {
        foreach (var tool in ReadTools())
            Assert.Single(tool.DeclaredSources());
    }

    [Fact]
    public void FileBasedApp_ResolvesToExistingProject()
    {
        var entry = Resolve("greet");

        var project = Assert.IsType<BundleInstaller.ProjectEntry>(entry);
        Assert.Equal("src/greet.cs", project.Relative);
        Assert.True(File.Exists(project.Full));
    }

    [Fact]
    public void CsprojApp_ResolvesToExistingProject()
    {
        var entry = Resolve("hello");

        var project = Assert.IsType<BundleInstaller.ProjectEntry>(entry);
        Assert.Equal("src/hello/hello.csproj", project.Relative);
        Assert.True(File.Exists(project.Full));
    }

    [Fact]
    public void Repository_ResolvesToRepositoryEntry()
    {
        var entry = Resolve("dotnet-inspect");

        var repository = Assert.IsType<BundleInstaller.RepositoryEntry>(entry);
        Assert.Equal("richlander/dotnet-inspect", repository.Repository);
    }

    [Fact]
    public void Package_ResolvesToPackageEntry()
    {
        var tools = ReadTools();
        var tool = tools.Single(t => t.Package is not null);

        var entry = Assert.IsType<BundleInstaller.PackageEntry>(
            ResolveOrFail(tool, tools.Count));

        Assert.Equal("dotnet-runtimeinfo", entry.Spec);
    }

    /// <summary>
    /// The four entries must resolve to three different kinds. Composition is the
    /// point of the fixture: a manifest that silently collapsed to one kind would
    /// still pass every per-entry assertion above.
    /// </summary>
    [Fact]
    public void Manifest_ComposesThreeEntryKinds()
    {
        var tools = ReadTools();

        var kinds = tools
            .Select(t => ResolveOrFail(t, tools.Count).GetType())
            .ToList();

        Assert.Equal(4, kinds.Count);
        Assert.Equal(3, kinds.Distinct().Count());
        Assert.Equal(2, kinds.Count(k => k == typeof(BundleInstaller.ProjectEntry)));
    }

    static BundleInstaller.Entry Resolve(string name)
    {
        var tools = ReadTools();
        return ResolveOrFail(tools.Single(t => t.Name == name), tools.Count);
    }

    /// <summary>
    /// <see cref="BundleInstaller.Resolve"/> returns null after printing an error.
    /// Every entry in a committed fixture is expected to resolve, so null is a
    /// fixture bug and should fail loudly rather than surface as a cast error.
    /// </summary>
    static BundleInstaller.Entry ResolveOrFail(Tool tool, int toolCount)
    {
        var entry = BundleInstaller.Resolve(tool, FixtureRoot, toolCount);
        Assert.NotNull(entry);
        return entry!;
    }
}
