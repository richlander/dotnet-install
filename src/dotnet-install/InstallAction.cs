/// <summary>
/// Handles the default install command logic — local project, NuGet package,
/// or GitHub repo. Extracted from Program.cs for use with System.CommandLine.
/// </summary>
static class InstallAction
{
    public static async Task<int> RunAsync(
        string[] projectArgs,
        string? packageSpec,
        string? gitSpec,
        string? projectPath,
        string? outputDir,
        bool useLocalBin,
        bool useSsh,
        bool allowRollForward,
        bool requireSourceLink)
    {
        string installDir = outputDir
            ?? (useLocalBin ? Installer.LocalBinDir : Installer.DefaultInstallDir);

        // --package and --github are single-item explicit sources
        if (packageSpec is not null)
        {
            int r = await Installer.InstallPackageAsync(packageSpec, installDir, allowRollForward, requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(installDir);
            return r;
        }

        if (gitSpec is not null)
        {
            int r = GitSource.InstallFromGit(gitSpec, installDir, useSsh, projectPath, requireSourceLink);
            if (r == 0) ShellHint.PrintIfNeeded(installDir);
            return r;
        }

        // No positional args — run setup if needed, otherwise show help
        if (projectArgs.Length == 0)
        {
            // PATH not configured at all — run doctor to set up
            if (!ShellConfig.IsOnPath(installDir) && !ShellConfig.IsConfiguredButNotActive(installDir))
            {
                return await DoctorCommand.Run(installDir, fix: true);
            }

            var rootCommand = CommandLineBuilder.CreateRootCommand();
            HelpWriter.WriteHelp(rootCommand);

            // PATH is in rc file but not active in this session (ephemeral shell)
            if (!ShellConfig.IsOnPath(installDir) && !UserConfig.Read(installDir).TipQuiet)
            {
                var config = ShellConfig.Detect(installDir);
                Console.WriteLine();
                Console.WriteLine($"tip: {config.DisplayDir} is not in this shell's PATH.");
                Console.WriteLine($"     Run: source {config.RcFile}");
                Console.WriteLine($"     To silence: dotnet-install config tip.quiet true");
            }

            return 0;
        }

        // Multiple args — install each one (skip prompts when multiple specified)
        int failures = 0;
        foreach (string arg in projectArgs)
        {
            int result = await InstallOneAsync(arg, installDir, useSsh, projectPath, allowRollForward, requireSourceLink);
            if (result != 0)
                failures++;
        }

        if (failures == 0)
            ShellHint.PrintIfNeeded(installDir);

        return failures > 0 ? 1 : 0;
    }

    static async Task<int> InstallOneAsync(
        string projectArg, string installDir, bool useSsh,
        string? projectPath, bool allowRollForward, bool requireSourceLink)
    {
        // Local path
        if (Directory.Exists(projectArg) || File.Exists(projectArg))
        {
            if (!CheckPrereqs(dotnet: true))
                return 1;

            string? projectFile = FindProjectFile(projectArg);
            if (projectFile is null)
            {
                Console.Error.WriteLine($"error: no project file found in '{projectArg}'");
                return 1;
            }
            return Installer.Install(projectFile, installDir, CreateLocalSource(projectFile), requireSourceLink);
        }

        // owner/repo pattern
        if (projectArg.Contains('/'))
        {
            string ownerRepo = projectArg.Split('@')[0];
            string url = useSsh
                ? $"git@github.com:{ownerRepo}.git"
                : $"https://github.com/{ownerRepo}";

            if (!CheckPrereqs(git: true, dotnet: true, context: url))
                return 1;

            return GitSource.InstallFromGit(projectArg, installDir, useSsh, projectPath, requireSourceLink);
        }

        // Bare name — treat as NuGet package
        return await Installer.InstallPackageAsync(projectArg, installDir, allowRollForward, requireSourceLink);
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
