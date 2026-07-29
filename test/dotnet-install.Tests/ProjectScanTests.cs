namespace dotnet_install.Tests;

// Pointing at a repo root (`dotnet-install .`) must resolve projects that live under
// src/, tools/, etc. rather than failing because nothing sits at the top level.
// Only executable projects are candidates, and build output is never scanned.
public class ProjectScanTests
{
    static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dni-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void WriteProject(string path, string? outputType)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string prop = outputType is null ? "" : $"<OutputType>{outputType}</OutputType>";
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                {prop}
              </PropertyGroup>
            </Project>
            """);
    }

    [Fact]
    public void FindsExecutableProject_NestedUnderSrc()
    {
        string root = NewTempDir();
        try
        {
            WriteProject(Path.Combine(root, "src", "tool", "tool.csproj"), "Exe");

            var found = InstallAction.FindExecutableProjects(root);

            Assert.Equal(Path.Combine(root, "src", "tool", "tool.csproj"), Assert.Single(found));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SkipsLibraryProjects()
    {
        string root = NewTempDir();
        try
        {
            WriteProject(Path.Combine(root, "src", "lib", "lib.csproj"), "Library");
            // No OutputType defaults to Library for the plain SDK.
            WriteProject(Path.Combine(root, "src", "other", "other.csproj"), null);

            Assert.Empty(InstallAction.FindExecutableProjects(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SkipsBuildOutputAndVendorDirectories()
    {
        string root = NewTempDir();
        try
        {
            WriteProject(Path.Combine(root, "src", "tool", "tool.csproj"), "Exe");
            WriteProject(Path.Combine(root, "artifacts", "stale", "stale.csproj"), "Exe");
            WriteProject(Path.Combine(root, "src", "tool", "obj", "gen", "gen.csproj"), "Exe");
            WriteProject(Path.Combine(root, "node_modules", "pkg", "pkg.csproj"), "Exe");
            WriteProject(Path.Combine(root, ".worktrees", "wt", "wt.csproj"), "Exe");

            Assert.Equal(Path.Combine(root, "src", "tool", "tool.csproj"),
                Assert.Single(InstallAction.FindExecutableProjects(root)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MultipleProjects_AreReturnedShallowestFirst()
    {
        string root = NewTempDir();
        try
        {
            WriteProject(Path.Combine(root, "src", "b", "b.csproj"), "Exe");
            WriteProject(Path.Combine(root, "src", "a", "a.csproj"), "Exe");
            WriteProject(Path.Combine(root, "top.csproj"), "Exe");

            var found = InstallAction.FindExecutableProjects(root);

            Assert.Equal(3, found.Count);
            Assert.Equal(Path.Combine(root, "top.csproj"), found[0]);
            Assert.Equal(Path.Combine(root, "src", "a", "a.csproj"), found[1]);
            Assert.Equal(Path.Combine(root, "src", "b", "b.csproj"), found[2]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void WebAndWorkerSdks_CountAsExecutable()    {
        string root = NewTempDir();
        try
        {
            string proj = Path.Combine(root, "src", "web", "web.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(proj)!);
            File.WriteAllText(proj, """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            Assert.Equal(proj, Assert.Single(InstallAction.FindExecutableProjects(root)));
        }
        finally { Directory.Delete(root, true); }
    }
}
