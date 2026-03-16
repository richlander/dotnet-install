static class SetupCommand
{
    /// <summary>
    /// Set up dotnet-install: create self-link in install dir and configure shell PATH.
    /// Designed to be idempotent — safe to run multiple times.
    /// </summary>
    public static int Run(string installDir)
    {
        installDir = Path.GetFullPath(installDir);
        Directory.CreateDirectory(installDir);

        Console.WriteLine();
        Console.WriteLine("  Setting up dotnet-install...");
        Console.WriteLine();

        bool didSomething = false;

        // Step 1: Self-link — ensure dotnet-install is accessible from the install directory
        didSomething |= EnsureSelfLink(installDir);

        // Step 2: Shell PATH configuration
        didSomething |= ConfigureShellPath(installDir);

        if (!didSomething)
        {
            Console.WriteLine("  ✔ Already set up — nothing to do.");
        }

        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Create a symlink (or copy) from the install directory to the running binary,
    /// so dotnet-install is accessible from ~/.dotnet/bin/ for host dispatch.
    /// </summary>
    static bool EnsureSelfLink(string installDir)
    {
        string? processPath = Environment.ProcessPath;
        if (processPath is null)
        {
            Console.Error.WriteLine("  warning: could not determine process path — skipping self-link.");
            return false;
        }

        processPath = Path.GetFullPath(processPath);
        string selfName = "dotnet-install";
        string targetPath = Path.Combine(installDir, selfName);

        // Already in the install directory — nothing to do
        string processDir = Path.GetDirectoryName(processPath)!;
        if (string.Equals(Path.GetFullPath(processDir), installDir, StringComparison.Ordinal))
        {
            Console.WriteLine($"  ✔ dotnet-install is in {DisplayPath(installDir)}");
            return false;
        }

        // Check if a link/binary already exists and points to the right place
        if (File.Exists(targetPath))
        {
            var info = new FileInfo(targetPath);
            if (info.LinkTarget is not null &&
                string.Equals(Path.GetFullPath(info.LinkTarget), processPath, StringComparison.Ordinal))
            {
                Console.WriteLine($"  ✔ {DisplayPath(targetPath)} → {DisplayPath(processPath)}");
                return false;
            }

            // Exists but wrong target — replace
            File.Delete(targetPath);
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows: copy the binary (symlinks require elevated privileges)
            File.Copy(processPath, targetPath, overwrite: true);
            Console.WriteLine($"  ✔ Copied dotnet-install to {DisplayPath(installDir)}");
        }
        else
        {
            File.CreateSymbolicLink(targetPath, processPath);
            Console.WriteLine($"  ✔ {DisplayPath(targetPath)} → {DisplayPath(processPath)}");
        }

        return true;
    }

    /// <summary>
    /// Detect the user's shell and offer to write PATH configuration to the rc file.
    /// </summary>
    static bool ConfigureShellPath(string installDir)
    {
        if (ShellConfig.IsOnPath(installDir))
        {
            Console.WriteLine($"  ✔ {DisplayPath(installDir)} is on PATH");
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return ConfigureWindowsPath(installDir);
        }

        var config = ShellConfig.Detect(installDir);

        if (config.RcFile is null)
        {
            Console.WriteLine($"  ⚠ {config.DisplayDir} is not in your PATH.");
            Console.WriteLine($"  Add it to your shell config:");
            Console.WriteLine();
            Console.WriteLine($"    {config.ExportLine}");
            Console.WriteLine();
            return false;
        }

        // Check if the rc file already has the PATH entry
        if (config.RcFileContainsPath())
        {
            Console.WriteLine($"  ✔ {config.RcFile} already configures PATH");
            Console.WriteLine($"    Restart your shell or run: source {config.RcFile}");
            return false;
        }

        // Prompt the user
        if (Console.IsInputRedirected)
        {
            // Non-interactive: just write it
            return WritePathToRcFile(config);
        }

        Console.Write($"  Add {config.DisplayDir} to PATH in {config.RcFile}? [Y/n] ");
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();

        if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
        {
            Console.WriteLine($"  Skipped. Add it manually:");
            Console.WriteLine();
            Console.WriteLine($"    echo '{config.RcLine}' >> {config.RcFile}");
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

        Console.WriteLine($"  ✔ Added PATH to {config.RcFile}");
        Console.WriteLine();
        Console.WriteLine($"    Restart your shell or run: source {config.RcFile}");

        return true;
    }

    static bool ConfigureWindowsPath(string installDir)
    {
        string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";

        if (currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(Path.GetFullPath(p.TrimEnd(Path.DirectorySeparatorChar)),
                                     installDir.TrimEnd(Path.DirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"  ✔ {DisplayPath(installDir)} is in user PATH");
            return false;
        }

        if (!Console.IsInputRedirected)
        {
            Console.Write($"  Add {DisplayPath(installDir)} to user PATH? [Y/n] ");
            var key = Console.ReadKey(intercept: true);
            Console.WriteLine();

            if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
            {
                Console.WriteLine($"  Skipped.");
                return false;
            }
        }

        string newPath = string.IsNullOrEmpty(currentPath)
            ? installDir
            : $"{installDir};{currentPath}";

        Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
        Console.WriteLine($"  ✔ Added {DisplayPath(installDir)} to user PATH");
        Console.WriteLine($"    Restart your terminal to pick up the change.");

        return true;
    }

    static string DisplayPath(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Replace(home, "~");
    }
}
