using System.Diagnostics;

static class SetupCommand
{
    /// <summary>
    /// Set up dotnet-install: ensure local binary, configure shell PATH, shed bootstrap scaffolding.
    /// Designed to be idempotent — safe to run multiple times.
    /// </summary>
    public static async Task<int> Run(string installDir)
    {
        installDir = Path.GetFullPath(installDir);
        Directory.CreateDirectory(installDir);

        Console.WriteLine();
        Console.WriteLine("Setting up dotnet-install...");
        Console.WriteLine();

        bool didSomething = false;

        // Step 1: Ensure dotnet-install is installed in the install directory with full pedigree
        didSomething |= await EnsureLocalInstallAsync(installDir);

        // Step 2: Shell PATH configuration
        didSomething |= ConfigureShellPath(installDir);

        // Step 3: Shed bootstrap scaffolding (dotnet tool) if no longer needed
        didSomething |= ShedDotnetTool(installDir);

        if (!didSomething)
        {
            Console.WriteLine("✔ Already set up — nothing to do.");
        }

        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Ensure dotnet-install is installed locally with full pedigree (NuGet metadata,
    /// .tool.json, version tracking). If running from an external location (e.g. dotnet
    /// tool .store), self-install from NuGet so the binary is standalone and updatable.
    /// </summary>
    public static async Task<bool> EnsureLocalInstallAsync(string installDir)
    {
        string selfName = "dotnet-install";
        string targetPath = Path.Combine(installDir, selfName);

        // Already have a local binary — nothing to do
        if (File.Exists(targetPath))
        {
            string? processPath = Environment.ProcessPath;
            if (processPath is not null)
            {
                string processDir = Path.GetDirectoryName(Path.GetFullPath(processPath))!;
                if (string.Equals(processDir, installDir, StringComparison.Ordinal))
                {
                    Console.WriteLine($"✔ dotnet-install is in {DisplayPath(installDir)}");
                    return false;
                }
            }

            Console.WriteLine($"✔ dotnet-install is in {DisplayPath(installDir)}");
            return false;
        }

        // No local binary — self-install from NuGet with full pedigree
        Console.WriteLine($"Installing dotnet-install to {DisplayPath(installDir)}...");
        int result = await Installer.InstallPackageAsync(selfName, installDir);
        return result == 0;
    }

    /// <summary>
    /// If dotnet-install was bootstrapped via `dotnet tool install -g` and a local
    /// binary now exists, remove the dotnet tool version — it's no longer needed.
    /// </summary>
    static bool ShedDotnetTool(string installDir)
    {
        // Check if dotnet-install is registered as a dotnet tool
        var checkPsi = new ProcessStartInfo("dotnet", ["tool", "list", "-g"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var checkProcess = Process.Start(checkPsi);
        if (checkProcess is null)
            return false;

        string output = checkProcess.StandardOutput.ReadToEnd();
        checkProcess.WaitForExit();

        if (checkProcess.ExitCode != 0 || !output.Contains("dotnet-install", StringComparison.OrdinalIgnoreCase))
            return false;

        // Only shed if we have a working local binary
        string localBinary = Path.Combine(installDir, "dotnet-install");
        if (!File.Exists(localBinary))
            return false;

        Console.WriteLine();
        Console.WriteLine("Removing bootstrap dotnet tool...");
        var psi = new ProcessStartInfo("dotnet", ["tool", "uninstall", "-g", "dotnet-install"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return false;

        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            Console.WriteLine("✔ Removed dotnet tool (no longer needed)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detect the user's shell and offer to write PATH configuration to the rc file.
    /// </summary>
    static bool ConfigureShellPath(string installDir)
    {
        if (ShellConfig.IsOnPath(installDir))
        {
            Console.WriteLine($"✔ {DisplayPath(installDir)} is on PATH");
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return ConfigureWindowsPath(installDir);
        }

        var config = ShellConfig.Detect(installDir);

        if (config.RcFile is null)
        {
            Console.WriteLine($"⚠ {config.DisplayDir} is not in your PATH.");
            Console.WriteLine($"Add it to your shell config:");
            Console.WriteLine();
            Console.WriteLine($"  {config.ExportLine}");
            Console.WriteLine();
            return false;
        }

        // Check if the rc file already has the PATH entry
        if (config.RcFileContainsPath())
        {
            Console.WriteLine($"✔ {config.RcFile} already configures PATH");
            Console.WriteLine($"  Restart your shell or run: source {config.RcFile}");
            return false;
        }

        // Prompt the user
        if (Console.IsInputRedirected)
        {
            // Non-interactive: just write it
            return WritePathToRcFile(config);
        }

        Console.Write($"Add {config.DisplayDir} to PATH in {config.RcFile}? [Y/n] ");
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();

        if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
        {
            Console.WriteLine($"Skipped. Add it manually:");
            Console.WriteLine();
            Console.WriteLine($"  echo '{config.RcLine}' >> {config.RcFile}");
            Console.WriteLine();
            return false;
        }

        return WritePathToRcFile(config);
    }

    static bool WritePathToRcFile(ShellConfig config)
    {
        string rcPath = config.RcFileAbsolute!;

        // Ensure parent directory exists (fish config path)
        string? rcDir = Path.GetDirectoryName(rcPath);
        if (rcDir is not null)
            Directory.CreateDirectory(rcDir);

        // Append with a blank line separator
        string existing = File.Exists(rcPath) ? File.ReadAllText(rcPath) : "";
        string separator = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "";
        string comment = $"\n# Added by dotnet-install setup";
        File.AppendAllText(rcPath, $"{separator}{comment}\n{config.RcLine}\n");

        Console.WriteLine($"✔ Added PATH to {config.RcFile}");
        Console.WriteLine();
        Console.WriteLine($"  Restart your shell or run: source {config.RcFile}");

        return true;
    }

    static bool ConfigureWindowsPath(string installDir)
    {
        // Check if DOTNET_INSTALL_HOME is already set
        string? existingHome = Environment.GetEnvironmentVariable(ShellConfig.EnvVar, EnvironmentVariableTarget.User);
        bool homeSet = existingHome is not null &&
            string.Equals(Path.GetFullPath(existingHome.TrimEnd(Path.DirectorySeparatorChar)),
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
            return false;
        }

        if (!Console.IsInputRedirected)
        {
            Console.Write($"Configure {ShellConfig.EnvVar} and PATH? [Y/n] ");
            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();

            if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
            {
                Console.WriteLine($"Skipped.");
                return false;
            }
        }

        if (!homeSet)
        {
            Environment.SetEnvironmentVariable(ShellConfig.EnvVar, installDir, EnvironmentVariableTarget.User);
            Console.WriteLine($"✔ Set {ShellConfig.EnvVar}={DisplayPath(installDir)}");
        }

        if (!pathSet)
        {
            string newPath = string.IsNullOrEmpty(currentPath)
                ? installDir
                : $"{installDir};{currentPath}";
            Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
            Console.WriteLine($"✔ Added {DisplayPath(installDir)} to user PATH");
        }

        Console.WriteLine($"  Restart your terminal to pick up the change.");
        return true;
    }

    static string DisplayPath(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Replace(home, "~");
    }
}
