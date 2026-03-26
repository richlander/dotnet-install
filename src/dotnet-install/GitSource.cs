using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

static class GitSource
{
    static string CacheBase => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nuget", "git-tools");

    public static int InstallFromUrl(string url, string installDir, string? branch, string? tag, string? rev, string? projectOverride, bool requireSourceLink = false, bool quiet = false)
    {
        string? gitRef = rev ?? tag ?? branch;
        bool pinned = rev is not null || tag is not null;

        // Derive a cache key from the URL
        string cacheKey = url.Replace("://", "/").Replace(":", "/").TrimEnd('/').TrimEnd(".git".ToCharArray());
        string repoCache = Path.Combine(CacheBase, "_git", Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(cacheKey)))[..16].ToLowerInvariant());
        string repoDir = Path.Combine(repoCache, "repo");
        Directory.CreateDirectory(repoCache);

        // Clone or fetch
        bool isExistingClone = Directory.Exists(Path.Combine(repoDir, ".git"));

        if (isExistingClone)
        {
            if (!quiet) Console.WriteLine($"Fetching {url}...");
            if (Run("git", ["-C", repoDir, "fetch", "origin"]) != 0)
                return 1;
        }
        else
        {
            if (!quiet) Console.WriteLine($"Cloning {url}...");
            if (Run("git", ["clone", url, repoDir]) != 0)
                return 1;
        }

        // Checkout ref
        if (gitRef is not null)
        {
            if (!quiet) Console.WriteLine($"Checking out {gitRef}...");
            if (Run("git", ["-C", repoDir, "checkout", gitRef]) != 0)
            {
                if (Run("git", ["-C", repoDir, "checkout", "--detach", $"origin/{gitRef}"]) != 0)
                {
                    Console.Error.WriteLine($"error: could not resolve ref '{gitRef}'");
                    return 1;
                }
            }
        }
        else if (isExistingClone)
        {
            // Detect force push on default branch update
            string? defaultRef = RunCapture("git", ["-C", repoDir, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"]);
            if (defaultRef is not null)
            {
                string defaultBranch = defaultRef.Trim().Replace("origin/", "");
                string? localHead = RunCapture("git", ["-C", repoDir, "rev-parse", "HEAD"])?.Trim();
                string? remoteHead = RunCapture("git", ["-C", repoDir, "rev-parse", $"origin/{defaultBranch}"])?.Trim();

                if (localHead is not null && remoteHead is not null && localHead != remoteHead)
                {
                    int ancestorCheck = Run("git", ["-C", repoDir, "merge-base", "--is-ancestor", localHead, $"origin/{defaultBranch}"]);
                    if (ancestorCheck != 0)
                    {
                        Console.Error.WriteLine("error: remote history has diverged (force push detected)");
                        Console.Error.WriteLine("Uninstall and reinstall the tool to continue:");
                        Console.Error.WriteLine($"  dotnet-install rm <tool>");
                        Console.Error.WriteLine($"  dotnet-install --git {url}");
                        return 1;
                    }
                }

                Run("git", ["-C", repoDir, "checkout", defaultBranch]);
                Run("git", ["-C", repoDir, "reset", "--hard", $"origin/{defaultBranch}"]);
            }
        }

        // Capture commit SHA for provenance tracking
        string? commitSha = RunCapture("git", ["-C", repoDir, "rev-parse", "HEAD"])?.Trim();

        var config = ToolConfig.Read(repoDir);

        string? projectFile = DiscoverProject(repoDir, projectOverride);
        if (projectFile is null)
            return 1;

        var source = new InstallSource
        {
            Type = "git",
            Repository = url,
            Ref = gitRef,
            Commit = commitSha,
            Project = projectOverride,
            Pinned = pinned
        };

        return Installer.Install(projectFile, installDir, source, requireSourceLink, quiet, update: config?.Update);
    }

    public static int InstallFromGit(string spec, string installDir, bool useSsh, string? branch, string? tag, string? rev, string? projectOverride, bool requireSourceLink = false, bool quiet = false)
    {
        // Parse owner/repo[@ref]
        int atIndex = spec.IndexOf('@');
        string ownerRepo = atIndex >= 0 ? spec[..atIndex] : spec;
        string? specRef = atIndex >= 0 ? spec[(atIndex + 1)..] : null;

        // Explicit flags override @ref in the spec string
        string? gitRef = rev ?? tag ?? branch ?? specRef;
        // @ref, --tag, and --rev are pinned; --branch is not
        bool pinned = rev is not null || tag is not null || specRef is not null;

        int slashIndex = ownerRepo.IndexOf('/');
        if (slashIndex < 0)
        {
            Console.Error.WriteLine("error: expected format owner/repo[@ref]");
            return 1;
        }

        string owner = ownerRepo[..slashIndex];
        string repo = ownerRepo[(slashIndex + 1)..];

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            Console.Error.WriteLine("error: owner and repo must not be empty");
            return 1;
        }

        // Resolve cache paths
        string repoCache = Path.Combine(CacheBase, owner, repo);
        string repoDir = Path.Combine(repoCache, "repo");
        Directory.CreateDirectory(repoCache);

        string cloneUrl = useSsh
            ? $"git@github.com:{owner}/{repo}.git"
            : $"https://github.com/{owner}/{repo}.git";

        // Clone or fetch
        bool isExistingClone = Directory.Exists(Path.Combine(repoDir, ".git"));

        if (isExistingClone)
        {
            if (!quiet) Console.WriteLine($"Fetching {owner}/{repo}...");
            if (Run("git", ["-C", repoDir, "fetch", "origin"]) != 0)
                return 1;
        }
        else
        {
            if (!quiet) Console.WriteLine($"Cloning {owner}/{repo}...");
            if (Run("git", ["clone", cloneUrl, repoDir]) != 0)
                return 1;
        }

        // Checkout ref
        if (gitRef is not null)
        {
            if (!quiet) Console.WriteLine($"Checking out {gitRef}...");
            if (Run("git", ["-C", repoDir, "checkout", gitRef]) != 0)
            {
                if (Run("git", ["-C", repoDir, "checkout", "--detach", $"origin/{gitRef}"]) != 0)
                {
                    Console.Error.WriteLine($"error: could not resolve ref '{gitRef}'");
                    return 1;
                }
            }
        }
        else if (isExistingClone)
        {
            // Detect force push: if the local HEAD is not an ancestor of the
            // remote branch, history was rewritten. Refuse to update silently —
            // the user must uninstall and reinstall.
            string? defaultRef = RunCapture("git", ["-C", repoDir, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"]);
            if (defaultRef is not null)
            {
                string defaultBranch = defaultRef.Trim().Replace("origin/", "");
                string? localHead = RunCapture("git", ["-C", repoDir, "rev-parse", "HEAD"])?.Trim();
                string? remoteHead = RunCapture("git", ["-C", repoDir, "rev-parse", $"origin/{defaultBranch}"])?.Trim();

                if (localHead is not null && remoteHead is not null && localHead != remoteHead)
                {
                    int ancestorCheck = Run("git", ["-C", repoDir, "merge-base", "--is-ancestor", localHead, $"origin/{defaultBranch}"]);
                    if (ancestorCheck != 0)
                    {
                        Console.Error.WriteLine("error: remote history has diverged (force push detected)");
                        Console.Error.WriteLine("Uninstall and reinstall the tool to continue:");
                        Console.Error.WriteLine($"  dotnet-install rm <tool>");
                        Console.Error.WriteLine($"  dotnet-install --github {spec}");
                        return 1;
                    }
                }

                Run("git", ["-C", repoDir, "checkout", defaultBranch]);
                Run("git", ["-C", repoDir, "reset", "--hard", $"origin/{defaultBranch}"]);
            }
        }

        // Capture commit SHA for provenance tracking
        string? commitSha = RunCapture("git", ["-C", repoDir, "rev-parse", "HEAD"])?.Trim();

        // Read repo config (.dotnet-install.json) for exe name and update plan
        var config = ToolConfig.Read(repoDir);

        // Discover project
        string? projectFile = DiscoverProject(repoDir, projectOverride);
        if (projectFile is null)
            return 1;

        var source = new InstallSource
        {
            Type = "github",
            Repository = $"{owner}/{repo}",
            Ref = gitRef,
            Commit = commitSha,
            Ssh = useSsh,
            Project = projectOverride,
            Pinned = pinned
        };

        return Installer.Install(projectFile, installDir, source, requireSourceLink, quiet, update: config?.Update);
    }

    // ---- Project discovery ----

    static string? DiscoverProject(string repoDir, string? projectOverride)
    {
        // 1. CLI override
        if (projectOverride is not null)
        {
            string full = Path.GetFullPath(Path.Combine(repoDir, projectOverride));
            if (!File.Exists(full))
            {
                Console.Error.WriteLine($"error: project not found: {projectOverride}");
                return null;
            }
            return full;
        }

        // 2. Repo manifest (.dotnet-install.json)
        string manifest = Path.Combine(repoDir, ".dotnet-install.json");
        if (File.Exists(manifest))
        {
            try
            {
                var config = JsonSerializer.Deserialize(File.ReadAllText(manifest), ManifestContext.Default.InstallManifest);
                if (config?.Project is not null)
                {
                    string full = Path.GetFullPath(Path.Combine(repoDir, config.Project));
                    if (File.Exists(full))
                        return full;
                    Console.Error.WriteLine($"error: project from .dotnet-install.json not found: {config.Project}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: reading .dotnet-install.json: {ex.Message}");
                return null;
            }
        }

        // 3. Auto-detect: find project files with OutputType=Exe (excluding test projects)
        //    Also detect file-based apps (.cs files with #:property directives)
        List<string> exeProjects = [];

        foreach (string csproj in Directory.EnumerateFiles(repoDir, "*.*proj", SearchOption.AllDirectories))
        {
            try
            {
                var doc = XDocument.Load(csproj);
                var props = doc.Descendants()
                    .Where(e => e.Parent?.Name.LocalName == "PropertyGroup");

                string? outputType = props.FirstOrDefault(e => e.Name.LocalName == "OutputType")?.Value;
                if (!string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip test projects
                string? isTestProject = props.FirstOrDefault(e => e.Name.LocalName == "IsTestProject")?.Value;
                if (string.Equals(isTestProject, "true", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? isPackable = props.FirstOrDefault(e => e.Name.LocalName == "IsPackable")?.Value;
                if (string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase))
                    continue;

                exeProjects.Add(csproj);
            }
            catch
            {
                // Skip unparseable project files
            }
        }

        // Scan for file-based apps (.cs files with #:property directives)
        if (exeProjects.Count == 0)
        {
            foreach (string csFile in Directory.EnumerateFiles(repoDir, "*.cs", SearchOption.AllDirectories))
            {
                // Skip files in obj/bin directories
                string relativePath = Path.GetRelativePath(repoDir, csFile);
                if (relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                if (Installer.IsFileBasedApp(csFile) && Installer.ParseFileBasedProperties(csFile).Count > 0)
                    exeProjects.Add(csFile);
            }
        }

        if (exeProjects.Count == 1)
            return exeProjects[0];

        if (exeProjects.Count == 0)
        {
            Console.Error.WriteLine("error: no executable projects found in repository");
            return null;
        }

        return ProjectSelector.Select(exeProjects, repoDir);
    }

    // ---- Process helpers ----

    static int Run(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo { FileName = fileName };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        p!.WaitForExit();
        return p.ExitCode;
    }

    static string? RunCapture(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        string output = p!.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 ? output : null;
    }
}

// ---- JSON source generation for AOT ----

class InstallManifest
{
    [JsonPropertyName("project")]
    public string? Project { get; set; }
}

[JsonSerializable(typeof(InstallManifest))]
partial class ManifestContext : JsonSerializerContext { }
