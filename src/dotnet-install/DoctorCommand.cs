using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Validate dotnet-install environment. Reports issues by default; --fix applies remediation.
/// </summary>
static class DoctorCommand
{
    internal static readonly string Ok = OperatingSystem.IsWindows() ? "-" : "✔";
    internal static readonly string Warn = OperatingSystem.IsWindows() ? "-" : "⚠";

    public static async Task<int> Run(string installDir, bool fix = false, bool pathOnly = false)
    {
        installDir = Path.GetFullPath(installDir);
        Directory.CreateDirectory(installDir);

        int issues = 0;

        Console.WriteLine();

        // Step 1: Shell PATH configuration (always first — most important for usability)
        issues += CheckShellPath(installDir, fix);

        if (pathOnly)
            return 0;

        // Step 2: Repo-local tools (<git-root>/.dotnet/bin), when present in the CWD's repo
        issues += CheckRepoLocalBin(installDir, fix);

        // Step 3: Drain global tools if configured
        var config = UserConfig.Read(installDir);
        if (config.ManageGlobalTools)
        {
            issues += await DrainGlobalToolsAsync(installDir, fix);
        }

        if (issues > 0 && !fix)
        {
            Console.WriteLine();
            Console.WriteLine($"Found {issues} issue(s). Run `dotnet-install doctor --fix` to repair.");
        }

        return 0;
    }

    /// <summary>
    /// Check PATH configuration: is install dir on PATH, is rc file configured?
    /// </summary>
    static int CheckShellPath(string installDir, bool fix)
    {
        if (OperatingSystem.IsWindows())
            return CheckWindowsPath(installDir, fix);

        var shellConfig = ShellConfig.Detect(installDir);

        if (ShellConfig.IsOnPath(installDir))
        {
            return 0;
        }

        if (shellConfig.RcFile is not null && shellConfig.RcFileContainsPath())
        {
            // Rc file references the install dir, but the env file it sources may be
            // missing (e.g. deleted, or configured by a legacy inline export). Ensure
            // it exists so the source command below actually works.
            if (!File.Exists(shellConfig.EnvFileAbsolute))
                WriteEnvFile(shellConfig);

            Console.WriteLine();
            Console.WriteLine($"To configure your current shell, run:");
            Console.WriteLine();
            Console.WriteLine($"{shellConfig.SourceCommand}");
            return 0;
        }

        // Not configured
        if (shellConfig.RcFile is null)
        {
            // Still write the env file so the user can source it
            WriteEnvFile(shellConfig);

            Console.WriteLine();
            Console.WriteLine($"{Warn} {shellConfig.DisplayDir} is not on PATH");
            Console.WriteLine($"To activate in this shell:");
            Console.WriteLine($"  {shellConfig.SourceCommand}");
            Console.WriteLine($"To configure permanently, add that line to your shell config.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"{shellConfig.DisplayDir} needs to be added to PATH in {shellConfig.RcFile}.");

        if (!fix)
        {
            Console.WriteLine($"Run with --fix to add to {shellConfig.RcFile}");
            return 1;
        }

        // Fix mode: write to rc file (auto-write when non-interactive, prompt otherwise)
        if (Console.IsInputRedirected)
        {
            WritePathToRcFile(shellConfig);
            return 0;
        }

        Console.Write($"Add {shellConfig.DisplayDir} to PATH in {shellConfig.RcFile}? [Y/n] ");
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();

        if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
        {
            Console.WriteLine($"Skipped. Add manually: echo '{shellConfig.RcLine}' >> {shellConfig.RcFile}");
            return 1;
        }

        WritePathToRcFile(shellConfig);
        return 0;
    }

    /// <summary>
    /// Report on a repo-local install directory (&lt;git-root&gt;/.dotnet/bin) when the
    /// current directory is inside a repo that has one. Repo-local tools are a
    /// worktree-scoped alternative to the global install: they are activated
    /// transiently (by sourcing the env file) rather than via the global rc file,
    /// so this only checks that the directory is git-ignored and offers a
    /// per-shell activation line — it never writes to the user's shell profile.
    /// </summary>
    static int CheckRepoLocalBin(string primaryInstallDir, bool fix)
    {
        string? root = FindGitRoot(Directory.GetCurrentDirectory());
        if (root is null)
            return 0;

        string binDir = Path.Combine(root, ".dotnet", "bin");
        if (!Directory.Exists(binDir))
            return 0;

        // If the primary check already targeted this directory (e.g. `doctor -o
        // .dotnet/bin`), don't report it twice.
        if (PathsEqual(primaryInstallDir, binDir))
            return 0;

        int issues = 0;
        string display = Path.GetRelativePath(Directory.GetCurrentDirectory(), binDir);

        Console.WriteLine();
        Console.WriteLine($"Repo-local tools: {display}");

        // 1. Is the directory kept out of source control?
        if (IsPathGitIgnored(root, binDir))
        {
            Console.WriteLine($"{Ok} .dotnet/ is git-ignored");
        }
        else if (fix)
        {
            AddToGitignore(root);
            Console.WriteLine($"{Ok} Added .dotnet/ to .gitignore");
        }
        else
        {
            Console.WriteLine($"{Warn} .dotnet/ is not git-ignored");
            Console.WriteLine("  Run with --fix to add it to .gitignore");
            issues++;
        }

        // 2. Is it usable in this shell? Repo-local activation is transient — we
        //    write the env file and show the source line; we never touch the rc file.
        if (ShellConfig.IsOnPath(binDir))
        {
            Console.WriteLine($"{Ok} on PATH for this shell");
        }
        else if (OperatingSystem.IsWindows())
        {
            Console.WriteLine($"{Warn} not on PATH for this shell");
            Console.WriteLine("  Activate for this session:");
            Console.WriteLine($"    $env:PATH = \"{binDir};$env:PATH\"");
            issues++;
        }
        else
        {
            var shellConfig = ShellConfig.Detect(binDir);
            if (!File.Exists(shellConfig.EnvFileAbsolute))
                WriteEnvFile(shellConfig);

            string envFile = Path.GetRelativePath(Directory.GetCurrentDirectory(), shellConfig.EnvFileAbsolute);
            string sourceCommand = shellConfig.ShellName == "fish"
                ? $"source {envFile}"
                : $". {envFile}";

            Console.WriteLine($"{Warn} not on PATH for this shell");
            Console.WriteLine("  Activate for this shell (transient — not added to your shell profile):");
            Console.WriteLine($"    {sourceCommand}");
            issues++;
        }

        return issues;
    }

    /// <summary>Walk up from <paramref name="start"/> to the git repo root (worktree-aware: `.git` may be a file).</summary>
    internal static string? FindGitRoot(string start)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir is not null)
        {
            string gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>True if <paramref name="path"/> is ignored by git (honors all .gitignore rules).</summary>
    static bool IsPathGitIgnored(string root, string path)
    {
        try
        {
            var psi = new ProcessStartInfo("git", ["-C", root, "check-ignore", "-q", path])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            p.WaitForExit();
            return p.ExitCode == 0; // 0 = path is ignored
        }
        catch
        {
            return false;
        }
    }

    internal static void AddToGitignore(string root)
    {
        string gitignore = Path.Combine(root, ".gitignore");
        string existing = File.Exists(gitignore) ? File.ReadAllText(gitignore) : "";
        string separator = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
        File.AppendAllText(gitignore, $"{separator}\n# Added by dotnet-install (repo-local tools)\n.dotnet/\n");
    }

    static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static void WriteEnvFile(ShellConfig config) => config.WriteEnvFile();

    static void WritePathToRcFile(ShellConfig config)
    {
        // Write the env file (create/overwrite)
        string envPath = config.EnvFileAbsolute;
        string? envDir = Path.GetDirectoryName(envPath);
        if (envDir is not null)
            Directory.CreateDirectory(envDir);
        File.WriteAllText(envPath, config.EnvFileContent);

        // Append source line to rc file
        string rcPath = config.RcFileAbsolute!;

        string? rcDir = Path.GetDirectoryName(rcPath);
        if (rcDir is not null)
            Directory.CreateDirectory(rcDir);

        string existing = File.Exists(rcPath) ? File.ReadAllText(rcPath) : "";
        string separator = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
        string comment = "\n# Added by dotnet-install";
        File.AppendAllText(rcPath, $"{separator}{comment}\n{config.RcLine}\n");

        Console.WriteLine($"{Ok} Added");
        Console.WriteLine();
        Console.WriteLine($"To configure your current shell, run:");
        Console.WriteLine();
        Console.WriteLine($"{config.SourceCommand}");
    }

    static int CheckWindowsPath(string installDir, bool fix)
    {
        string? envHome = Environment.GetEnvironmentVariable(ShellConfig.EnvVar, EnvironmentVariableTarget.User);
        bool homeSet = envHome is not null &&
            string.Equals(Path.GetFullPath(envHome.TrimEnd(Path.DirectorySeparatorChar)),
                          installDir.TrimEnd(Path.DirectorySeparatorChar),
                          StringComparison.OrdinalIgnoreCase);

        string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        bool pathSet = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(Path.GetFullPath(p.TrimEnd(Path.DirectorySeparatorChar)),
                                     installDir.TrimEnd(Path.DirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase));

        if (homeSet && pathSet)
        {
            return 0;
        }

        if (!fix)
        {
            if (!homeSet)
                Console.WriteLine($"{Warn} {ShellConfig.EnvVar} is not set");
            if (!pathSet)
                Console.WriteLine($"{Warn} {DisplayPath(installDir)} is not in user PATH");
            return 1;
        }

        if (!Console.IsInputRedirected)
        {
            Console.Write($"Configure {ShellConfig.EnvVar} and PATH? [Y/n] ");
            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();

            if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
                return 1;
        }

        if (!homeSet)
            Environment.SetEnvironmentVariable(ShellConfig.EnvVar, installDir, EnvironmentVariableTarget.User);

        if (!pathSet)
        {
            string newPath = string.IsNullOrEmpty(currentPath)
                ? installDir
                : $"{installDir};{currentPath}";
            Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
        }

        Console.WriteLine($"{Ok} Added");
        Console.WriteLine();
        Console.WriteLine("Restart your terminal to apply PATH changes.");
        return 0;
    }

    /// <summary>
    /// Drain dotnet global tools: reinstall each via dotnet-install, then remove the dotnet tool.
    /// Only removes a dotnet tool after dotnet-install successfully installs it.
    /// </summary>
    static async Task<int> DrainGlobalToolsAsync(string installDir, bool fix)
    {
        var tools = ListDotnetGlobalTools();
        if (tools is null || tools.Count == 0) return 0;

        // Filter out dotnet-install itself (already handled by shed)
        var candidates = tools
            .Where(t => !t.PackageId.Equals("dotnet-install", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine($"{Ok} No dotnet global tools to drain");
            return 0;
        }

        Console.WriteLine($"{Warn} {candidates.Count} dotnet global tool(s) to drain");

        if (!fix)
        {
            foreach (var tool in candidates)
                Console.WriteLine($"  {tool.PackageId} {tool.Version}");
            return 1;
        }

        int drained = 0;
        int failed = 0;
        foreach (var tool in candidates)
        {
            Console.WriteLine($"  Installing {tool.PackageId}...");

            int result = await Installer.InstallPackageAsync(tool.PackageId, installDir, quiet: true);

            if (result != 0)
            {
                Console.WriteLine($"  {Warn} Failed to install {tool.PackageId} — skipping");
                failed++;
                continue;
            }

            // Successfully installed — now safe to remove the dotnet tool
            var psi = new ProcessStartInfo("dotnet", ["tool", "uninstall", "-g", tool.PackageId])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process is not null)
            {
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    Console.WriteLine($"  {Ok} {tool.PackageId} {tool.Version}");
                    drained++;
                }
                else
                {
                    Console.WriteLine($"  {Warn} Installed {tool.PackageId} but failed to remove dotnet tool");
                    failed++;
                }
            }
        }

        if (drained > 0)
            Console.WriteLine($"  Drained {drained} tool(s)");

        return failed > 0 ? 1 : 0;
    }

    /// <summary>
    /// Parse output of `dotnet tool list -g --format json`.
    /// Returns null if dotnet SDK is not available.
    /// </summary>
    static List<DotnetGlobalTool>? ListDotnetGlobalTools()
    {
        ProcessStartInfo psi;
        try
        {
            psi = new ProcessStartInfo("dotnet", ["tool", "list", "-g", "--format", "json"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
        }
        catch
        {
            return null;
        }

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        if (process is null) return null;

        using var _ = process;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) return null;

        try
        {
            var result = JsonSerializer.Deserialize(output, DotnetToolListContext.Default.DotnetToolListOutput);
            return result?.Data;
        }
        catch
        {
            return null;
        }
    }

    static string DisplayPath(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Replace(home, "~");
    }
}

// --- JSON models for `dotnet tool list -g --format json` ---

class DotnetToolListOutput
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("data")]
    public List<DotnetGlobalTool> Data { get; set; } = [];
}

class DotnetGlobalTool
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = [];
}

[JsonSerializable(typeof(DotnetToolListOutput))]
partial class DotnetToolListContext : JsonSerializerContext { }
