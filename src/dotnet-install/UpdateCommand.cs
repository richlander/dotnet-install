using System.Diagnostics;
using NuGetFetch;

static class UpdateCommand
{
    public static async Task<int> RunAsync(string installDir, string[] args)
    {
        if (!Directory.Exists(installDir))
        {
            Console.WriteLine("No tools installed.");
            return 0;
        }

        // Determine which tools to update
        var toolNames = args.Where(a => !a.StartsWith('-')).ToList();
        bool updateAll = toolNames.Count == 0;

        var tools = DiscoverTools(installDir);

        if (tools.Count == 0)
        {
            Console.WriteLine("No tools with update metadata found.");
            return 0;
        }

        if (!updateAll)
        {
            tools = tools.Where(t => toolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            var missing = toolNames.Where(n => !tools.Any(t => t.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (string name in missing)
                Console.Error.WriteLine($"{name}: not found or no update metadata");
        }

        if (tools.Count == 0)
        {
            Console.WriteLine("Nothing to update.");
            return 0;
        }

        int failures = 0;

        foreach (var tool in tools)
        {
            var source = tool.Manifest.Source!;

            switch (source.Type)
            {
                case "nuget":
                    if (await UpdateNuGetAsync(tool, installDir) != 0)
                        failures++;
                    break;

                case "github":
                    if (UpdateGitHub(tool, installDir) != 0)
                        failures++;
                    break;

                case "local":
                    if (UpdateLocal(tool, installDir) != 0)
                        failures++;
                    break;

                default:
                    Console.Error.WriteLine($"{tool.Name}: unknown source type '{source.Type}'");
                    failures++;
                    break;
            }
        }

        return failures > 0 ? 1 : 0;
    }

    // ---- NuGet update ----

    static async Task<int> UpdateNuGetAsync(ToolInfo tool, string installDir)
    {
        var source = tool.Manifest.Source!;
        string packageName = source.Package!;
        string installedVersion = source.Version!;

        Console.Write($"{tool.Name} ({packageName} {installedVersion})... ");

        using var client = new HttpClient();
        var nuget = new NuGetClient(client);

        string? latestVersion = await nuget.GetLatestVersionAsync(packageName);
        if (latestVersion is null)
        {
            Console.WriteLine("package not found");
            return 1;
        }

        if (string.Equals(latestVersion, installedVersion, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("up to date");
            return 0;
        }

        Console.WriteLine($"{installedVersion} -> {latestVersion}");
        return await Installer.InstallPackageAsync(
            $"{packageName}@{latestVersion}", installDir, tool.Manifest.RollForward);
    }

    // ---- GitHub update ----

    static int UpdateGitHub(ToolInfo tool, string installDir)
    {
        var source = tool.Manifest.Source!;
        string repository = source.Repository!;
        string? gitRef = source.Ref;
        string? installedCommit = source.Commit;
        string shortCommit = installedCommit is not null && installedCommit.Length >= 7
            ? installedCommit[..7] : installedCommit ?? "unknown";

        Console.Write($"{tool.Name} ({repository} {shortCommit})... ");

        // Resolve cache paths
        int slashIndex = repository.IndexOf('/');
        string owner = repository[..slashIndex];
        string repo = repository[(slashIndex + 1)..];
        string cacheBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "git-tools");
        string repoDir = Path.Combine(cacheBase, owner, repo, "repo");

        if (!Directory.Exists(Path.Combine(repoDir, ".git")))
        {
            Console.WriteLine("not cached, reinstalling");
            string spec = gitRef is not null ? $"{repository}@{gitRef}" : repository;
            return GitSource.InstallFromGit(spec, installDir, source.Ssh, source.Project);
        }

        // Fetch latest
        if (Run("git", ["-C", repoDir, "fetch", "origin"]) != 0)
        {
            Console.WriteLine("fetch failed");
            return 1;
        }

        // Resolve the remote ref to compare
        string refToCheck = gitRef is not null ? $"origin/{gitRef}" : "HEAD";
        if (gitRef is null)
        {
            // Find default branch
            string? defaultRef = RunCapture("git", ["-C", repoDir, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"]);
            if (defaultRef is not null)
                refToCheck = defaultRef.Trim();
        }

        string? latestCommit = RunCapture("git", ["-C", repoDir, "rev-parse", refToCheck])?.Trim();
        if (latestCommit is null)
        {
            Console.WriteLine("could not resolve ref");
            return 1;
        }

        if (string.Equals(latestCommit, installedCommit, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("up to date");
            return 0;
        }

        string shortLatest = latestCommit.Length >= 7 ? latestCommit[..7] : latestCommit;
        Console.WriteLine($"{shortCommit} -> {shortLatest}");

        string spec2 = gitRef is not null ? $"{repository}@{gitRef}" : repository;
        return GitSource.InstallFromGit(spec2, installDir, source.Ssh, source.Project);
    }

    // ---- Local update ----

    static int UpdateLocal(ToolInfo tool, string installDir)
    {
        var source = tool.Manifest.Source!;
        string projectPath = source.Project!;
        string? installedCommit = source.Commit;
        string shortCommit = installedCommit is not null && installedCommit.Length >= 7
            ? installedCommit[..7] : installedCommit ?? "unknown";

        Console.Write($"{tool.Name} (local {shortCommit})... ");

        if (!File.Exists(projectPath))
        {
            Console.WriteLine($"project not found: {projectPath}");
            return 1;
        }

        // Check git status in the project's directory
        string? projectDir = Path.GetDirectoryName(projectPath);
        if (projectDir is null)
        {
            Console.WriteLine("could not determine project directory");
            return 1;
        }

        string? currentCommit = RunCapture("git", ["-C", projectDir, "rev-parse", "HEAD"])?.Trim();

        if (currentCommit is not null &&
            string.Equals(currentCommit, installedCommit, StringComparison.OrdinalIgnoreCase))
        {
            // Same commit — check for uncommitted changes
            string? status = RunCapture("git", ["-C", projectDir, "status", "--porcelain"]);
            if (string.IsNullOrWhiteSpace(status))
            {
                Console.WriteLine("up to date");
                return 0;
            }
            Console.WriteLine("uncommitted changes, rebuilding");
        }
        else if (currentCommit is not null)
        {
            string shortCurrent = currentCommit.Length >= 7 ? currentCommit[..7] : currentCommit;
            Console.WriteLine($"{shortCommit} -> {shortCurrent}");
        }
        else
        {
            Console.WriteLine("not a git repo, rebuilding");
        }

        var newSource = new InstallSource
        {
            Type = "local",
            Project = projectPath,
            Commit = currentCommit
        };

        return Installer.Install(projectPath, installDir, newSource);
    }

    // ---- Tool discovery ----

    record ToolInfo(string Name, ToolManifest Manifest);

    static List<ToolInfo> DiscoverTools(string installDir)
    {
        var tools = new List<ToolInfo>();

        foreach (string entry in Directory.GetDirectories(installDir))
        {
            string dirName = Path.GetFileName(entry);
            if (!dirName.StartsWith('_'))
                continue;

            string toolName = dirName[1..]; // strip leading underscore
            var manifest = ToolMetadata.Read(entry);
            if (manifest?.Source is not null)
                tools.Add(new ToolInfo(toolName, manifest));
        }

        return tools.OrderBy(t => t.Name).ToList();
    }

    // ---- Process helpers ----

    static int Run(string fileName, string[] args)
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
        p!.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
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
