/// <summary>
/// Handles the default install command logic. Every source — <c>--package</c>,
/// <c>--repo</c>, <c>--github</c>, <c>--project</c>, or a bare positional path —
/// installs to <c>~/.dotnet/bin</c>, matching <c>cargo install</c> and
/// <c>go install</c>: you point at a source, you get a command on PATH.
/// <c>-o</c> overrides the destination like any other tool's output flag.
/// <para>
/// The two repo gestures are kept distinct: <c>--repo</c> builds the toolset a repo
/// <i>advertises</i> via <c>.dotnet-install/.dotnet-install.json</c>, while a bare
/// path simply looks for a project. A bare path never reads the manifest.
/// </para>
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
        string installDir = outputDir
            ?? (useLocalBin ? Installer.LocalBinDir : Installer.DefaultInstallDir);

        int Report(int exitCode)
        {
            if (exitCode == 0)
                ShellHint.PrintIfNeeded(installDir);
            return exitCode;
        }

        // --package: NuGet install
        if (packageSpec is not null)
            return Report(await Installer.InstallPackageAsync(packageSpec, installDir, requireSourceLink));

        // --github: GitHub owner/repo shorthand (sugar over --repo). Requires an
        // advertised manifest unless --project names a project explicitly.
        if (githubSpec is not null)
        {
            if (!CheckPrereqs(git: true, dotnet: true))
                return 1;

            return Report(await GitSource.InstallFromGitAsync(githubSpec, installDir, useSsh, branch, tag, rev, projectPath, requireSourceLink));
        }

        // --repo: git URL (clone) or local repo path (built in place). Requires an
        // advertised manifest unless --project is given.
        if (repoSpec is not null)
        {
            if (Directory.Exists(repoSpec))
            {
                if (!CheckPrereqs(dotnet: true))
                    return 1;

                return Report(await InstallFromRepoAsync(repoSpec, installDir, projectPath, requireSourceLink));
            }

            if (!CheckPrereqs(git: true, dotnet: true))
                return 1;

            return Report(await GitSource.InstallFromUrlAsync(repoSpec, installDir, branch, tag, rev, projectPath, requireSourceLink));
        }

        // --project (standalone): explicit project path.
        if (projectArg is null && projectPath is not null)
        {
            if (!CheckPrereqs(dotnet: true))
                return 1;

            if (TryInstallLocalProject(projectPath, installDir, requireSourceLink) is int pr)
                return Report(pr);

            Console.Error.WriteLine($"error: no project file found in '{projectPath}'");
            return 1;
        }

        // A path (typically "."), or nothing at all — in which case the current
        // directory is the target, the way `dotnet publish` works. Naming a path is
        // what opts into the interactive selector; the bare command never prompts.
        if (!CheckPrereqs(dotnet: true))
            return 1;

        return Report(InstallFromPath(projectArg ?? ".", installDir, requireSourceLink,
            interactive: projectArg is not null));
    }

    /// <summary>
    /// A path to build from: an explicit positional path, or the current directory when
    /// the command was run bare. This gesture only ever looks for a project — it
    /// deliberately does <i>not</i> read <c>.dotnet-install/.dotnet-install.json</c>,
    /// which keeps it distinct from <c>--repo</c> (build the toolset a repo advertises).
    /// <para>
    /// <paramref name="interactive"/> gates the project selector. Naming a path is the
    /// opt-in; the bare command resolves or fails, and never starts a prompt the user
    /// did not ask for.
    /// </para>
    /// </summary>
    static int InstallFromPath(string path, string installDir, bool requireSourceLink, bool interactive)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Console.Error.WriteLine($"error: path not found: '{path}'");
            return 1;
        }

        string? projectFile = FindProjectFile(path, interactive);
        if (projectFile is null)
        {
            if (File.Exists(path))
            {
                Console.Error.WriteLine($"error: not a project file: '{path}'");
                return 1;
            }

            // A null from an ambiguous directory means the selector already reported
            // (or the user cancelled); only report when nothing was found at all.
            if (FindCandidates(path).Count == 0)
            {
                Console.Error.WriteLine(
                    $"error: no project file found in '{DisplayPath(path)}'; " +
                    "see 'dotnet-install --help'");
            }
            return 1;
        }

        return Installer.Install(projectFile, installDir, CreateLocalSource(projectFile), requireSourceLink);
    }

    static string DisplayPath(string path) =>
        path == "." ? Directory.GetCurrentDirectory() : path;

    /// <summary>
    /// A local repo path passed to <c>--repo</c>: built in place. Requires an advertised
    /// manifest (bundle or project) unless <c>--project</c> is given.
    /// </summary>
    static async Task<int> InstallFromRepoAsync(string dir, string installDir, string? projectOverride, bool requireSourceLink)
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
            return Installer.Install(projectFile, installDir, CreateLocalSource(projectFile), requireSourceLink);
        }

        var config = ToolConfig.ReadFromRepo(dir);
        var tools = config?.GetTools() ?? [];
        if (tools.Count == 0)
        {
            if (config is null)
                Console.Error.WriteLine($"error: '{dir}' does not advertise any tools ({ToolConfig.RepoDirName}/{ToolConfig.FileName} not found).");
            else
                Console.Error.WriteLine($"error: {ToolConfig.RepoDirName}/{ToolConfig.FileName} is present but advertises no tools.");
            Console.Error.WriteLine("Pass --project <path> to install a specific project from it.");
            return 1;
        }

        string fullDir = Path.GetFullPath(dir);
        var source = new InstallSource { Type = "local", Commit = GitCommit(fullDir) };
        return await BundleInstaller.InstallAsync(fullDir, tools, installDir, source, requireSourceLink);
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
    /// Installs a single tool from an explicit <c>--project</c> path (a directory or a
    /// project/file-based-app file). When the path is a directory whose
    /// <c>.dotnet-install/.dotnet-install.json</c> advertises exactly one tool with a
    /// <c>project</c>, that project (its command <c>name</c> and <c>update</c> channel)
    /// is honored; otherwise the path is scanned for a project file. Returns the exit
    /// code, or null if no project could be resolved (so the caller can report it).
    /// </summary>
    static int? TryInstallLocalProject(string path, string installDir, bool requireSourceLink)
    {
        var repoConfig = Directory.Exists(path) ? ToolConfig.ReadFromRepo(path) : null;

        if (repoConfig?.GetTools() is { Count: 1 } tools && tools[0].Project is { } toolProject)
        {
            string projectFile = Path.GetFullPath(Path.Combine(path, toolProject));
            if (!File.Exists(projectFile))
            {
                Console.Error.WriteLine($"error: project from {ToolConfig.RepoDirName}/{ToolConfig.FileName} not found: {toolProject}");
                return 1;
            }
            return Installer.Install(projectFile, installDir, CreateLocalSource(projectFile), requireSourceLink, commandName: tools[0].Name);
        }

        string? found = FindProjectFile(path);
        if (found is null)
            return null;

        return Installer.Install(found, installDir, CreateLocalSource(found), requireSourceLink);
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

    static string? FindProjectFile(string path, bool interactive = true)
    {
        if (File.Exists(path) && (IsProjectFile(path) || Installer.IsFileBasedApp(path)))
            return Path.GetFullPath(path);

        if (!Directory.Exists(path))
            return null;

        var projects = FindCandidates(path);

        if (projects.Count == 1)
            return projects[0];

        if (projects.Count > 1)
            return ProjectSelector.Select(projects, path, interactive);

        return null;
    }

    /// <summary>
    /// Candidate projects for a directory: project files or file-based apps sitting
    /// directly in it, else a recursive scan for executable projects (the repo-root
    /// case, where projects live under <c>src/</c> and friends).
    /// </summary>
    static List<string> FindCandidates(string dir)
    {
        var projects = Directory.GetFiles(dir, "*.*proj")
            .Where(IsProjectFile)
            .ToList();

        if (projects.Count == 0)
        {
            projects = Directory.GetFiles(dir, "*.cs")
                .Where(f => Installer.ParseFileBasedProperties(f).Count > 0)
                .ToList();
        }

        if (projects.Count == 0)
            projects = FindExecutableProjects(dir);

        return projects;
    }

    static bool IsProjectFile(string f) =>
        f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Directories that never contain source of interest and can be very large.
    /// </summary>
    static readonly string[] SkipDirectories =
        [".git", ".dotnet", ".worktrees", ".vs", ".idea", "bin", "obj", "artifacts", "node_modules", "packages"];

    /// <summary>
    /// Recursively finds executable projects under a directory, ordered so the
    /// shallowest (and then alphabetical) candidates come first. Only the project XML
    /// is inspected — a full MSBuild evaluation of every project in a repo would be
    /// far too slow for a scan.
    /// </summary>
    internal static List<string> FindExecutableProjects(string root)
    {
        var results = new List<string>();
        Walk(root, depth: 0);

        return results
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Walk(string dir, int depth)
        {
            // Deep trees are almost always vendored sources rather than the repo's
            // own tools, and the selector caps out well before this anyway.
            if (depth > 4 || results.Count > 64)
                return;

            string[] subdirs;
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*.*proj"))
                {
                    if ((file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                         file.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                         file.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) &&
                        Installer.IsExecutableProject(file))
                    {
                        results.Add(file);
                    }
                }

                subdirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            foreach (string sub in subdirs)
            {
                string name = Path.GetFileName(sub);
                if (name.Length == 0 || SkipDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;

                Walk(sub, depth + 1);
            }
        }
    }
}
