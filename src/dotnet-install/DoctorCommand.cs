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
        {
            if (IsBootstrapInstall())
            {
                Console.WriteLine();
                Console.WriteLine("Run the following to complete installation:");
                Console.WriteLine();
                Console.WriteLine("dotnet-install doctor --fix");
            }
            return 0;
        }

        bool isBootstrap = IsBootstrapInstall();

        if (fix && isBootstrap)
        {
            Console.WriteLine("Setting up dotnet-install (one-time)...");
        }

        // Step 2: Ensure dotnet-install binary is in the install directory
        issues += await CheckBinaryAsync(installDir, fix, isBootstrap);

        // Step 3: Shed bootstrap scaffolding (dotnet tool) if present
        issues += ShedBootstrapTool(installDir, fix);

        // Step 4: Drain global tools if configured
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
    /// Check if dotnet-install was bootstrapped via `dotnet tool install -g`.
    /// </summary>
    static bool IsBootstrapInstall()
    {
        var tools = ListDotnetGlobalTools();
        return tools?.Any(t =>
            t.PackageId.Equals("dotnet-install", StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>
    /// Check that dotnet-install binary exists in the install directory.
    /// Uses tool list discovery instead of raw File.Exists.
    /// </summary>
    static async Task<int> CheckBinaryAsync(string installDir, bool fix, bool isBootstrap)
    {
        if (IsToolInstalled(installDir, "dotnet-install"))
        {
            if (!isBootstrap)
                Console.WriteLine($"{Ok} dotnet-install is in {DisplayPath(installDir)}");
            return 0;
        }

        if (!fix)
        {
            Console.WriteLine($"{Warn} dotnet-install is not in {DisplayPath(installDir)}");
            return 1;
        }

        int result = await Installer.InstallPackageAsync("dotnet-install", installDir, quiet: true);
        if (result == 0)
            Console.WriteLine($"{Ok} Installed to {DisplayPath(installDir)}");
        else
            Console.WriteLine($"{Warn} Failed to install dotnet-install");
        return result == 0 ? 0 : 1;
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

    static void WriteEnvFile(ShellConfig config)
    {
        string envPath = config.EnvFileAbsolute;
        string? envDir = Path.GetDirectoryName(envPath);
        if (envDir is not null)
            Directory.CreateDirectory(envDir);
        File.WriteAllText(envPath, config.EnvFileContent);
    }

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
    /// If dotnet-install was bootstrapped via `dotnet tool install -g`, remove the dotnet tool version.
    /// </summary>
    static int ShedBootstrapTool(string installDir, bool fix)
    {
        var tools = ListDotnetGlobalTools();
        if (tools is null) return 0;

        var selfTool = tools.FirstOrDefault(t =>
            t.PackageId.Equals("dotnet-install", StringComparison.OrdinalIgnoreCase));

        if (selfTool is null) return 0;

        if (!IsToolInstalled(installDir, "dotnet-install")) return 0;

        if (!fix)
            return 1;

        // On Windows, the bootstrap .cmd shim is still being executed by the
        // calling shell. Uninstalling it mid-execution deletes the shim and
        // produces "The batch file cannot be found." Defer to the next run,
        // which will be invoked from ~/.dotnet/bin instead.
        if (OperatingSystem.IsWindows() && IsRunningFromDotnetTools())
            return 0;

        var psi = new ProcessStartInfo("dotnet", ["tool", "uninstall", "-g", "dotnet-install"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return 1;
        process.WaitForExit();

        // The exit code may be unreliable (e.g. on Windows the store cleanup
        // can fail even though the tool was removed). Verify with tool list.
        var remaining = ListDotnetGlobalTools();
        bool removed = remaining?.Any(t =>
            t.PackageId.Equals("dotnet-install", StringComparison.OrdinalIgnoreCase)) != true;

        if (removed)
        {
            Console.WriteLine($"{Ok} Removed from ~/.dotnet/tools");
            return 0;
        }

        Console.WriteLine($"{Warn} Failed to remove from ~/.dotnet/tools");
        return 1;
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

    /// <summary>
    /// Check if a tool is installed in the install directory using the same
    /// discovery logic as `dotnet-install ls`.
    /// </summary>
    static bool IsToolInstalled(string installDir, string toolName)
    {
        if (!Directory.Exists(installDir)) return false;

        return Directory.GetFileSystemEntries(installDir)
            .Select(p => new FileInfo(p))
            .Any(f => Path.GetFileNameWithoutExtension(f.Name)
                .Equals(toolName, StringComparison.OrdinalIgnoreCase)
                && IsExecutable(f));
    }

    /// <summary>
    /// Check if the current process is running from the dotnet global tools directory.
    /// </summary>
    static bool IsRunningFromDotnetTools()
    {
        string? exePath = Environment.ProcessPath;
        if (exePath is null) return false;

        string toolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet", "tools");

        return exePath.StartsWith(toolsDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if a command is available on PATH (like `which` / `where`).
    /// </summary>
    static string? Which(string command)
    {
        var psi = new ProcessStartInfo(
            OperatingSystem.IsWindows() ? "where" : "which",
            [command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            string output = process.StandardOutput.ReadLine() ?? "";
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    static bool IsExecutable(FileInfo f)
    {
        if (f.Name.StartsWith('_')) return false;
        if (f.LinkTarget is not null) return true;

        if (!OperatingSystem.IsWindows())
            return (f.UnixFileMode & UnixFileMode.UserExecute) != 0;

        return f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || f.Extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
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
