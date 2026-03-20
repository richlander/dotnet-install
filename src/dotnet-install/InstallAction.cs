/// <summary>
/// Handles the default install command logic — local project, NuGet package,
/// or GitHub repo. Extracted from Program.cs for use with System.CommandLine.
/// </summary>
static class InstallAction
{
    public static async Task<int> RunAsync(
        string? projectArg,
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

        int result;

        if (packageSpec is not null)
        {
            result = await Installer.InstallPackageAsync(packageSpec, installDir, allowRollForward, requireSourceLink);
        }
        else if (gitSpec is not null)
        {
            result = GitSource.InstallFromGit(gitSpec, installDir, useSsh, projectPath, requireSourceLink);
        }
        else if (projectArg is not null)
        {
            // Check if it's a local path or an unresolved name
            if (Directory.Exists(projectArg) || File.Exists(projectArg))
            {
                string? projectFile = FindProjectFile(projectArg);
                if (projectFile is null)
                {
                    Console.Error.WriteLine($"error: no project file found in '{projectArg}'");
                    return 1;
                }
                result = Installer.Install(projectFile, installDir, CreateLocalSource(projectFile), requireSourceLink);
            }
            else if (projectArg.Contains('/'))
            {
                // owner/repo pattern — prompt before cloning from GitHub
                string url = useSsh
                    ? $"git@github.com:{projectArg.Split('@')[0]}.git"
                    : $"https://github.com/{projectArg.Split('@')[0]}";

                if (!Console.IsInputRedirected)
                {
                    Console.Write($"  '{projectArg}' is not a local path. Clone from {url}? [Y/n] ");
                    var key = Console.ReadKey(intercept: true);
                    Console.WriteLine();

                    if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
                    {
                        Console.WriteLine("  Cancelled.");
                        return 1;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"  '{projectArg}' is not a local path. Use --github to install from GitHub in non-interactive mode.");
                    return 1;
                }

                result = GitSource.InstallFromGit(projectArg, installDir, useSsh, projectPath, requireSourceLink);
            }
            else
            {
                // Bare name — prompt before installing from NuGet
                if (!Console.IsInputRedirected)
                {
                    Console.Write($"  '{projectArg}' is not a local path. Install from NuGet? [Y/n] ");
                    var key = Console.ReadKey(intercept: true);
                    Console.WriteLine();

                    if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
                    {
                        Console.WriteLine("  Cancelled.");
                        return 1;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"  '{projectArg}' is not a local path. Use --package to install from NuGet in non-interactive mode.");
                    return 1;
                }

                result = await Installer.InstallPackageAsync(projectArg, installDir, allowRollForward, requireSourceLink);
            }
        }
        else
        {
            // No source specified — show help
            var rootCommand = CommandLineBuilder.CreateRootCommand();
            HelpWriter.WriteHelp(rootCommand);
            return 0;
        }

        if (result == 0)
            ShellHint.PrintIfNeeded(installDir);

        return result;
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
        if (File.Exists(path) && IsProjectFile(path))
            return Path.GetFullPath(path);

        if (Directory.Exists(path))
        {
            var projects = Directory.GetFiles(path, "*.*proj")
                .Where(IsProjectFile)
                .ToArray();

            if (projects.Length == 1)
                return projects[0];

            if (projects.Length > 1)
            {
                Console.Error.WriteLine($"error: multiple project files found in '{path}'. Specify one explicitly.");
                return null;
            }
        }

        return null;

        static bool IsProjectFile(string f) =>
            f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
    }
}
