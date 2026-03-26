/// <summary>
/// Handles the default install command logic — local project, NuGet package,
/// GitHub repo, or git URL. Explicit flags for each source, no heuristics.
/// </summary>
static class InstallAction
{
    public static async Task<int> RunAsync(
        string? projectArg,
        string? packageSpec,
        string? githubSpec,
        string? gitUrl,
        string? branch,
        string? tag,
        string? rev,
        string? projectPath,
        string? outputDir,
        bool useLocalBin,
        bool useSsh,
        bool allowRollForward,
        bool requireSourceLink)
    {
        string installDir = outputDir
            ?? (useLocalBin ? Installer.LocalBinDir : Installer.DefaultInstallDir);

        // --package: NuGet install
        if (packageSpec is not null)
        {
            int r = await Installer.InstallPackageAsync(packageSpec, installDir, allowRollForward, requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(installDir);
            return r;
        }

        // --github: GitHub owner/repo shorthand
        if (githubSpec is not null)
        {
            if (!CheckPrereqs(git: true, dotnet: true))
                return 1;

            int r = GitSource.InstallFromGit(githubSpec, installDir, useSsh, branch, tag, rev, projectPath, requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(installDir);
            return r;
        }

        // --git: arbitrary git URL
        if (gitUrl is not null)
        {
            if (!CheckPrereqs(git: true, dotnet: true))
                return 1;

            int r = GitSource.InstallFromUrl(gitUrl, installDir, branch, tag, rev, projectPath, requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(installDir);
            return r;
        }

        // Local project: positional arg, --project/--path, or current directory
        string? localPath = projectArg ?? projectPath;

        if (localPath is not null)
        {
            if (!CheckPrereqs(dotnet: true))
                return 1;

            string? projectFile = FindProjectFile(localPath);
            if (projectFile is null)
            {
                Console.Error.WriteLine($"error: no project file found in '{localPath}'");
                return 1;
            }

            int r = Installer.Install(projectFile, installDir, CreateLocalSource(projectFile), requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(installDir);
            return r;
        }

        // No source specified — try current directory
        string? cwdProject = FindProjectFile(".");
        if (cwdProject is not null)
        {
            if (!CheckPrereqs(dotnet: true))
                return 1;

            int r = Installer.Install(cwdProject, installDir, CreateLocalSource(cwdProject), requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(installDir);
            return r;
        }

        // Nothing to act on — show help
        var rootCommand = CommandLineBuilder.CreateRootCommand();
        HelpWriter.WriteHelp(rootCommand);
        return 0;
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

    static InstallSource CreateLocalSource(string projectFile)
    {
        string fullPath = Path.GetFullPath(projectFile);
        string? projectDir = Path.GetDirectoryName(fullPath);
        string? commit = null;

        if (projectDir is not null)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git")
                {
                    WorkingDirectory = projectDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("rev-parse");
                psi.ArgumentList.Add("HEAD");

                using var p = System.Diagnostics.Process.Start(psi);
                commit = p?.StandardOutput.ReadToEnd().Trim();
                p?.WaitForExit();
                if (p?.ExitCode != 0) commit = null;
            }
            catch { }
        }

        return new InstallSource
        {
            Type = "local",
            Project = fullPath,
            Commit = commit
        };
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
