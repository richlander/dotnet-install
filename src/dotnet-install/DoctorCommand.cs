using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Validate dotnet-install environment. Reports issues by default; --fix applies remediation.
/// </summary>
static class DoctorCommand
{
    public static async Task<int> Run(string installDir, bool fix = false)
    {
        installDir = Path.GetFullPath(installDir);
        Directory.CreateDirectory(installDir);

        int issues = 0;
        bool isBootstrap = IsBootstrapInstall();

        Console.WriteLine();

        if (fix && isBootstrap)
        {
            Console.WriteLine("dotnet-install needs to be re-installed to " +
                $"{DisplayPath(installDir)}.");
            Console.WriteLine("The dotnet global tool bootstrap is temporary " +
                "— this is a one-time setup.");
            Console.WriteLine();
        }

        // Step 1: Ensure dotnet-install binary is in the install directory
        issues += await CheckBinaryAsync(installDir, fix, isBootstrap);

        // Step 2: Shed bootstrap scaffolding (dotnet tool) if present
        issues += ShedBootstrapTool(installDir, fix);

        // Step 3: Shell PATH configuration
        issues += CheckShellPath(installDir, fix);

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
    /// </summary>
    static async Task<int> CheckBinaryAsync(string installDir, bool fix, bool isBootstrap)
    {
        string targetPath = Path.Combine(installDir, "dotnet-install");

        if (File.Exists(targetPath))
        {
            if (!isBootstrap)
                Console.WriteLine($"✔ dotnet-install is in {DisplayPath(installDir)}");
            return 0;
        }

        if (!fix)
        {
            Console.WriteLine($"⚠ dotnet-install is not in {DisplayPath(installDir)}");
            return 1;
        }

        Console.WriteLine($"Installing dotnet-install to {DisplayPath(installDir)}...");
        int result = await Installer.InstallPackageAsync("dotnet-install", installDir, quiet: true);
        if (result == 0)
            Console.WriteLine($"✔ Installed dotnet-install");
        else
            Console.WriteLine($"⚠ Failed to install dotnet-install");
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
            Console.WriteLine($"Restart your shell or run: source {shellConfig.RcFile}");
            return 0;
        }

        // Not configured
        if (shellConfig.RcFile is null)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠ {shellConfig.DisplayDir} is not on PATH");
            Console.WriteLine($"  Add to your shell config:");
            Console.WriteLine($"    {shellConfig.EnvLine}");
            Console.WriteLine($"    {shellConfig.ExportLine}");
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

        Console.Write($"Add to {shellConfig.RcFile}? [Y/n] ");
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

    static void WritePathToRcFile(ShellConfig config)
    {
        string rcPath = config.RcFileAbsolute!;

        string? rcDir = Path.GetDirectoryName(rcPath);
        if (rcDir is not null)
            Directory.CreateDirectory(rcDir);

        string existing = File.Exists(rcPath) ? File.ReadAllText(rcPath) : "";
        string separator = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
        string comment = "\n# Added by dotnet-install";
        File.AppendAllText(rcPath, $"{separator}{comment}\n{config.RcLine}\n");

        Console.WriteLine($"✔ Added PATH to {config.RcFile}");
        Console.WriteLine();
        Console.WriteLine($"Restart your shell or run: source {config.RcFile}");
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
            Console.WriteLine($"✔ {ShellConfig.EnvVar} is set");
            Console.WriteLine($"✔ {DisplayPath(installDir)} is in user PATH");
            return 0;
        }

        if (!homeSet)
            Console.WriteLine($"⚠ {ShellConfig.EnvVar} is not set");
        if (!pathSet)
            Console.WriteLine($"⚠ {DisplayPath(installDir)} is not in user PATH");

        if (!fix)
            return 1;

        if (!Console.IsInputRedirected)
        {
            Console.Write($"  Configure {ShellConfig.EnvVar} and PATH? [Y/n] ");
            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();

            if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
                return 1;
        }

        if (!homeSet)
        {
            Environment.SetEnvironmentVariable(ShellConfig.EnvVar, installDir, EnvironmentVariableTarget.User);
            Console.WriteLine($"  ✔ Set {ShellConfig.EnvVar}={DisplayPath(installDir)}");
        }

        if (!pathSet)
        {
            string newPath = string.IsNullOrEmpty(currentPath)
                ? installDir
                : $"{installDir};{currentPath}";
            Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
            Console.WriteLine($"  ✔ Added {DisplayPath(installDir)} to user PATH");
        }

        Console.WriteLine($"  Restart your terminal to pick up the change.");
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

        string localBinary = Path.Combine(installDir, "dotnet-install");
        if (!File.Exists(localBinary)) return 0;

        Console.WriteLine("Removing bootstrap dotnet tool (no longer needed)...");

        if (!fix)
            return 1;

        var psi = new ProcessStartInfo("dotnet", ["tool", "uninstall", "-g", "dotnet-install"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return 1;
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            Console.WriteLine("✔ Removed");
            return 0;
        }

        Console.WriteLine("⚠ Failed to remove bootstrap dotnet tool");
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
            Console.WriteLine("✔ No dotnet global tools to drain");
            return 0;
        }

        Console.WriteLine($"⚠ {candidates.Count} dotnet global tool(s) to drain");

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
                Console.WriteLine($"  ⚠ Failed to install {tool.PackageId} — skipping");
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
                    Console.WriteLine($"  ✔ {tool.PackageId} {tool.Version}");
                    drained++;
                }
                else
                {
                    Console.WriteLine($"  ⚠ Installed {tool.PackageId} but failed to remove dotnet tool");
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
