/// <summary>
/// Handles the default install command logic. Two directions:
/// <list type="bullet">
/// <item><b>Outside requests</b> (<c>--repo</c>, <c>--github</c>, <c>--project</c>,
/// <c>--package</c>) point at something to get the tool for use — installed globally.</item>
/// <item><b>Inside request</b> (a bare positional path, typically <c>.</c>) honors a repo's
/// own advertised tooling — installed locally to <c>&lt;dir&gt;/.dotnet/bin</c>. With no
/// manifest it degrades to an outside request (global) interactively, or errors when piped.</item>
/// </list>
/// </summary>
static class InstallAction
{
    public static async Task<int> RunAsync(
        string? projectArg,
        string? packageSpec,
        string? githubSpec,
        string? repoSpec,
        string? branch,
        string? tag,
        string? rev,
        string? projectPath,
        string? outputDir,
        bool useLocalBin,
        bool useSsh,
        bool requireSourceLink)
    {
        string globalDir = outputDir
            ?? (useLocalBin ? Installer.LocalBinDir : Installer.DefaultInstallDir);

        // ---- Outside requests: install globally ----

        // --package: NuGet install
        if (packageSpec is not null)
        {
            int r = await Installer.InstallPackageAsync(packageSpec, globalDir, requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(globalDir);
            return r;
        }

        // --github: GitHub owner/repo shorthand (sugar over --repo). Requires an
        // advertised manifest unless --project names a project explicitly.
        if (githubSpec is not null)
        {
            if (!CheckPrereqs(git: true, dotnet: true))
                return 1;

            int r = GitSource.InstallFromGit(githubSpec, globalDir, useSsh, branch, tag, rev, projectPath, requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(globalDir);
            return r;
        }

        // --repo: git URL (clone) or local repo path (build in place). Global install;
        // requires an advertised manifest unless --project is given.
        if (repoSpec is not null)
        {
            if (Directory.Exists(repoSpec))
            {
                if (!CheckPrereqs(dotnet: true))
                    return 1;

                int r = InstallOutsideRepo(repoSpec, globalDir, projectPath, requireSourceLink);
                if (r == 0) ShellHint.PrintIfNeeded(globalDir);
                return r;
            }

            if (!CheckPrereqs(git: true, dotnet: true))
                return 1;

            int gr = GitSource.InstallFromUrl(repoSpec, globalDir, branch, tag, rev, projectPath, requireSourceLink);
            if (gr == 0) ShellHint.PrintIfNeeded(globalDir);
            return gr;
        }

        // --project (standalone): explicit project path → global.
        if (projectArg is null && projectPath is not null)
        {
            if (!CheckPrereqs(dotnet: true))
                return 1;

            if (TryInstallLocalProject(projectPath, globalDir, requireSourceLink) is int pr)
            {
                if (pr == 0) ShellHint.PrintIfNeeded(globalDir);
                return pr;
            }

            Console.Error.WriteLine($"error: no project file found in '{projectPath}'");
            return 1;
        }

        // ---- Inside request: positional path (typically ".") ----
        if (projectArg is not null)
        {
            if (!CheckPrereqs(dotnet: true))
                return 1;

            return InstallInside(projectArg, outputDir, globalDir, requireSourceLink);
        }

        // Nothing specified — show help.
        var rootCommand = CommandLineBuilder.CreateRootCommand();
        HelpWriter.WriteHelp(rootCommand);
        return 0;
    }

    /// <summary>
    /// The inside gesture (<c>dotnet-install .</c>): when the directory advertises tools via
    /// <c>.dotnet-install/</c>, install them locally to <c>&lt;dir&gt;/.dotnet/bin</c>. With no
    /// manifest, treat it as an outside request → global: interactively scan/pick a project,
    /// or (when piped) error and point at the explicit gesture.
    /// </summary>
    static int InstallInside(string dir, string? outputOverride, string globalDir, bool requireSourceLink)
    {
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"error: directory not found: '{dir}'");
            return 1;
        }

        var config = ToolConfig.ReadFromRepo(dir);
        bool advertised = config is { Bundle.Count: > 0 } || config?.Project is not null;

        if (advertised)
        {
            string localDir = outputOverride ?? Path.Combine(Path.GetFullPath(dir), ".dotnet", "bin");
            int r = TryInstallLocalBundle(dir, localDir, requireSourceLink)
                ?? TryInstallLocalProject(dir, localDir, requireSourceLink)
                ?? 1;
            if (r == 0) ShellHint.PrintRepoLocalActivation(localDir);
            return r;
        }

        // No manifest → outside request → global.
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine($"error: '{dir}' does not advertise any tools ({ToolConfig.RepoDirName}/{ToolConfig.FileName} not found).");
            Console.Error.WriteLine();
            Console.Error.WriteLine("To install a project from here globally, name it explicitly:");
            Console.Error.WriteLine($"  dotnet-install --project {dir}");
            return 1;
        }

        string targetDir = outputOverride ?? globalDir;
        string? projectFile = FindProjectFile(dir);
        if (projectFile is null)
        {
            Console.Error.WriteLine($"error: no project file found in '{dir}'");
            return 1;
        }

        int gr2 = Installer.Install(projectFile, targetDir, CreateLocalSource(projectFile), requireSourceLink);
        if (gr2 == 0) ShellHint.PrintIfNeeded(targetDir);
        return gr2;
    }

    /// <summary>
    /// A local repo path passed to <c>--repo</c>: built in place and installed globally.
    /// Requires an advertised manifest (bundle or project) unless <c>--project</c> is given.
    /// </summary>
    static int InstallOutsideRepo(string dir, string globalDir, string? projectOverride, bool requireSourceLink)
    {
        if (projectOverride is not null)
        {
            string proj = Path.Combine(dir, projectOverride);
            string? projectFile = FindProjectFile(proj);
            if (projectFile is null)
            {
                Console.Error.WriteLine($"error: project not found: {projectOverride}");
                return 1;
            }
            return Installer.Install(projectFile, globalDir, CreateLocalSource(projectFile), requireSourceLink);
        }

        var config = ToolConfig.ReadFromRepo(dir);
        bool advertised = config is { Bundle.Count: > 0 } || config?.Project is not null;
        if (!advertised)
        {
            Console.Error.WriteLine($"error: '{dir}' does not advertise any tools ({ToolConfig.RepoDirName}/{ToolConfig.FileName} not found).");
            Console.Error.WriteLine("Pass --project <path> to install a specific project from it.");
            return 1;
        }

        return TryInstallLocalBundle(dir, globalDir, requireSourceLink)
            ?? TryInstallLocalProject(dir, globalDir, requireSourceLink)
            ?? 1;
    }

    static bool CheckPrereqs(bool git = false, bool dotnet = false, string? context = null)
    {
        bool missingGit = git && !IsAvailable("git");
        bool missingDotnet = dotnet && !IsAvailable("dotnet");

        if (!missingGit && !missingDotnet)
            return true;

        if (context is not null)
            Console.Error.WriteLine($"Found: {context}");

        if (missingGit && missingDotnet)
            Console.Error.WriteLine("error: git and .NET SDK are not installed.");
        else if (missingGit)
            Console.Error.WriteLine("error: git is not installed.");
        else
            Console.Error.WriteLine("error: .NET SDK is not installed.");

        Console.Error.WriteLine();
        Console.Error.WriteLine("To resolve this:");

        if (missingGit)
            Console.Error.WriteLine("  Install git: https://git-scm.com");
        if (missingDotnet)
            Console.Error.WriteLine("  Install .NET SDK: https://dot.net/download");

        return false;

        static bool IsAvailable(string command)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(command)
                {
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                p?.WaitForExit();
                return p is not null && p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// If <paramref name="path"/> is a directory whose <c>.dotnet-install/.dotnet-install.json</c>
    /// advertises a bundle, builds and installs every listed project. Returns the exit
    /// code, or null if there is no advertised bundle to act on.
    /// </summary>
    static int? TryInstallLocalBundle(string path, string installDir, bool requireSourceLink)
    {
        if (!Directory.Exists(path))
            return null;

        var config = ToolConfig.ReadFromRepo(path);
        if (config?.Bundle is not { Count: > 0 } bundle)
            return null;

        string fullDir = Path.GetFullPath(path);
        var source = new InstallSource
        {
            Type = "local",
            Commit = GitCommit(fullDir)
        };

        return BundleInstaller.Install(fullDir, bundle, installDir, source, requireSourceLink);
    }

    /// <summary>
    /// Installs a single tool from a local directory or file path. When <paramref name="path"/>
    /// is a directory whose <c>.dotnet-install/.dotnet-install.json</c> advertises a
    /// <c>project</c>, that project (and its <c>update</c> channel) is used; otherwise the
    /// path is scanned for a project file. Returns the exit code, or null if no project
    /// could be resolved (so the caller can decide whether that is an error).
    /// </summary>
    static int? TryInstallLocalProject(string path, string installDir, bool requireSourceLink)
    {
        var repoConfig = Directory.Exists(path) ? ToolConfig.ReadFromRepo(path) : null;

        string? projectFile;
        if (repoConfig?.Project is not null)
        {
            projectFile = Path.GetFullPath(Path.Combine(path, repoConfig.Project));
            if (!File.Exists(projectFile))
            {
                Console.Error.WriteLine($"error: project from {ToolConfig.RepoDirName}/{ToolConfig.FileName} not found: {repoConfig.Project}");
                return 1;
            }
        }
        else
        {
            projectFile = FindProjectFile(path);
            if (projectFile is null)
                return null;
        }

        return Installer.Install(projectFile, installDir, CreateLocalSource(projectFile), requireSourceLink, update: repoConfig?.Update);
    }

    static InstallSource CreateLocalSource(string projectFile)
    {
        string fullPath = Path.GetFullPath(projectFile);
        string? projectDir = Path.GetDirectoryName(fullPath);

        return new InstallSource
        {
            Type = "local",
            Project = fullPath,
            Commit = projectDir is not null ? GitCommit(projectDir) : null
        };
    }

    /// <summary>Returns the HEAD commit SHA for a directory, or null if not a git repo.</summary>
    static string? GitCommit(string dir)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("HEAD");

            using var p = System.Diagnostics.Process.Start(psi);
            string? commit = p?.StandardOutput.ReadToEnd().Trim();
            p?.WaitForExit();
            return p?.ExitCode == 0 ? commit : null;
        }
        catch
        {
            return null;
        }
    }

    static string? FindProjectFile(string path)
    {
        if (File.Exists(path) && (IsProjectFile(path) || Installer.IsFileBasedApp(path)))
            return Path.GetFullPath(path);

        if (Directory.Exists(path))
        {
            var projects = Directory.GetFiles(path, "*.*proj")
                .Where(IsProjectFile)
                .ToList();

            // Also check for file-based apps if no project files found
            if (projects.Count == 0)
            {
                projects = Directory.GetFiles(path, "*.cs")
                    .Where(f => Installer.ParseFileBasedProperties(f).Count > 0)
                    .ToList();
            }

            if (projects.Count == 1)
                return projects[0];

            if (projects.Count > 1)
                return ProjectSelector.Select(projects, path);
        }

        return null;

        static bool IsProjectFile(string f) =>
            f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
    }
}
