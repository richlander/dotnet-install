using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Validate and fix dotnet-install environment: binary, PATH, shell config, global tool migration.
/// </summary>
static class DoctorCommand
{
    public static async Task<int> Run(string installDir)
    {
        installDir = Path.GetFullPath(installDir);
        Directory.CreateDirectory(installDir);

        Console.WriteLine();

        // Step 1: Ensure dotnet-install binary is in the install directory
        await SetupCommand.EnsureLocalInstallAsync(installDir);

        // Step 2: Shell PATH configuration
        CheckShellPath(installDir);

        // Step 3: Shed bootstrap scaffolding (dotnet tool) if present
        ShedBootstrapTool(installDir);

        // Step 4: Drain global tools if configured
        var config = UserConfig.Read(installDir);
        if (config.ManageGlobalTools)
        {
            await DrainGlobalToolsAsync(installDir);
        }

        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Check PATH configuration: is install dir on PATH, is rc file configured?
    /// </summary>
    static void CheckShellPath(string installDir)
    {
        if (OperatingSystem.IsWindows())
        {
            CheckWindowsPath(installDir);
            return;
        }

        var shellConfig = ShellConfig.Detect(installDir);

        if (ShellConfig.IsOnPath(installDir))
        {
            Console.WriteLine($"✔ {shellConfig.DisplayDir} is on PATH");
        }
        else if (shellConfig.RcFile is not null && shellConfig.RcFileContainsPath())
        {
            Console.WriteLine($"✔ {shellConfig.RcFile} configures PATH");
            Console.WriteLine($"  Restart your shell or run: source {shellConfig.RcFile}");
        }
        else if (shellConfig.RcFile is not null)
        {
            Console.WriteLine($"⚠ {shellConfig.DisplayDir} is not on PATH");

            if (Console.IsInputRedirected)
            {
                WritePathToRcFile(shellConfig);
            }
            else
            {
                Console.Write($"  Add to {shellConfig.RcFile}? [Y/n] ");
                var key = Console.ReadKey(intercept: true);
                Console.WriteLine();

                if (key.Key != ConsoleKey.Escape && key.KeyChar is not ('n' or 'N'))
                {
                    WritePathToRcFile(shellConfig);
                }
                else
                {
                    Console.WriteLine($"  Add manually: echo '{shellConfig.RcLine}' >> {shellConfig.RcFile}");
                }
            }
        }
        else
        {
            Console.WriteLine($"⚠ {shellConfig.DisplayDir} is not on PATH");
            Console.WriteLine($"  Add to your shell config:");
            Console.WriteLine($"    {shellConfig.EnvLine}");
            Console.WriteLine($"    {shellConfig.ExportLine}");
        }
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

        Console.WriteLine($"  ✔ Added PATH to {config.RcFile}");
        Console.WriteLine($"    Restart your shell or run: source {config.RcFile}");
    }

    static void CheckWindowsPath(string installDir)
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
            return;
        }

        if (!Console.IsInputRedirected)
        {
            Console.Write($"⚠ Configure {ShellConfig.EnvVar} and PATH? [Y/n] ");
            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();

            if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
                return;
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
    }

    /// <summary>
    /// If dotnet-install was bootstrapped via `dotnet tool install -g`, remove the dotnet tool version.
    /// </summary>
    static void ShedBootstrapTool(string installDir)
    {
        var tools = ListDotnetGlobalTools();
        if (tools is null) return;

        var selfTool = tools.FirstOrDefault(t =>
            t.PackageId.Equals("dotnet-install", StringComparison.OrdinalIgnoreCase));

        if (selfTool is null) return;

        string localBinary = Path.Combine(installDir, "dotnet-install");
        if (!File.Exists(localBinary)) return;

        Console.WriteLine("⚠ dotnet-install is still registered as a dotnet global tool");

        var psi = new ProcessStartInfo("dotnet", ["tool", "uninstall", "-g", "dotnet-install"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return;
        process.WaitForExit();

        if (process.ExitCode == 0)
            Console.WriteLine("  ✔ Removed bootstrap dotnet tool");
        else
            Console.WriteLine("  ⚠ Failed to remove bootstrap dotnet tool");
    }

    /// <summary>
    /// Drain dotnet global tools: reinstall each via dotnet-install, then remove the dotnet tool.
    /// Only removes a dotnet tool after dotnet-install successfully installs it.
    /// </summary>
    static async Task DrainGlobalToolsAsync(string installDir)
    {
        var tools = ListDotnetGlobalTools();
        if (tools is null || tools.Count == 0) return;

        // Filter out dotnet-install itself (already handled by shed)
        var candidates = tools
            .Where(t => !t.PackageId.Equals("dotnet-install", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine("✔ No dotnet global tools to drain");
            return;
        }

        Console.WriteLine($"⚠ {candidates.Count} dotnet global tool(s) to drain");

        int drained = 0;
        foreach (var tool in candidates)
        {
            Console.WriteLine($"  Installing {tool.PackageId}...");

            int result = await Installer.InstallPackageAsync(tool.PackageId, installDir, quiet: true);

            if (result != 0)
            {
                Console.WriteLine($"  ⚠ Failed to install {tool.PackageId} — skipping");
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
                }
            }
        }

        if (drained > 0)
            Console.WriteLine($"  Drained {drained} tool(s)");
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
