/// <summary>
/// Shell configuration detection — shared by ShellHint (print-only) and SetupCommand (write).
/// </summary>
record ShellConfig(string ShellName, string? RcFile, string? RcFileAbsolute, string ExportLine, string DisplayDir)
{
    public static bool IsOnPath(string dir)
    {
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

        return pathDirs.Any(p =>
            string.Equals(Path.GetFullPath(p.TrimEnd(Path.DirectorySeparatorChar)),
                          dir.TrimEnd(Path.DirectorySeparatorChar),
                          OperatingSystem.IsWindows()
                              ? StringComparison.OrdinalIgnoreCase
                              : StringComparison.Ordinal));
    }

    /// <summary>
    /// Detect the current shell and build configuration for the given install directory.
    /// </summary>
    public static ShellConfig Detect(string installDir)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string displayDir = installDir.Replace(home, "~");

        string shell = Environment.GetEnvironmentVariable("SHELL") ?? "";
        string shellName = Path.GetFileName(shell);

        string pathEntry = installDir.StartsWith(home + "/")
            ? $"$HOME/{Path.GetRelativePath(home, installDir)}"
            : installDir;
        string exportLine = $"""export PATH="{pathEntry}:$PATH" """.Trim();

        string? rcFile = shellName switch
        {
            "zsh" => "~/.zshrc",
            "bash" => GetBashRcFile(),
            "fish" => "~/.config/fish/config.fish",
            _ => null
        };

        string? rcFileAbsolute = rcFile?.Replace("~", home);

        return new ShellConfig(shellName, rcFile, rcFileAbsolute, exportLine, displayDir);
    }

    /// <summary>
    /// The line to append to the rc file (shell-specific syntax).
    /// </summary>
    public string RcLine => ShellName == "fish"
        ? $"fish_add_path {DisplayDir}"
        : ExportLine;

    /// <summary>
    /// Check if the rc file already contains the PATH configuration.
    /// </summary>
    public bool RcFileContainsPath()
    {
        if (RcFileAbsolute is null || !File.Exists(RcFileAbsolute))
            return false;

        string content = File.ReadAllText(RcFileAbsolute);
        // Check for the exact export line or the key path segment
        return content.Contains(DisplayDir.Replace("~", "$HOME")) ||
               content.Contains(DisplayDir);
    }

    static string GetBashRcFile()
    {
        // On macOS, bash reads ~/.bash_profile for login shells (default Terminal behavior)
        // On Linux, bash reads ~/.bashrc for interactive non-login shells
        if (OperatingSystem.IsMacOS())
            return "~/.bash_profile";

        return "~/.bashrc";
    }
}

static class ShellHint
{
    /// <summary>
    /// After a successful install, check if the install directory is on PATH.
    /// If not, print shell-specific instructions.
    /// </summary>
    public static void PrintIfNeeded(string installDir)
    {
        installDir = Path.GetFullPath(installDir);

        if (ShellConfig.IsOnPath(installDir))
            return;

        var config = ShellConfig.Detect(installDir);

        Console.WriteLine();
        Console.WriteLine($"  ⚠ {config.DisplayDir} is not in your PATH.");

        if (OperatingSystem.IsWindows())
        {
            PrintWindowsHint(config.DisplayDir, installDir);
        }
        else
        {
            PrintUnixHint(config);
        }
    }

    static void PrintUnixHint(ShellConfig config)
    {
        if (config.RcFile is not null)
        {
            Console.WriteLine($"  Add it with:");
            Console.WriteLine();

            if (config.ShellName == "fish")
                Console.WriteLine($"    echo '{config.RcLine}' >> {config.RcFile} && {config.RcLine}");
            else
                Console.WriteLine($"    echo '{config.RcLine}' >> {config.RcFile} && source {config.RcFile}");
        }
        else
        {
            Console.WriteLine($"  Add it to your shell config:");
            Console.WriteLine();
            Console.WriteLine($"    {config.ExportLine}");
        }

        Console.WriteLine();
    }

    static void PrintWindowsHint(string displayDir, string absoluteDir)
    {
        Console.WriteLine($"  Add it with:");
        Console.WriteLine();
        Console.WriteLine($"""    [Environment]::SetEnvironmentVariable("PATH", "{absoluteDir};" + [Environment]::GetEnvironmentVariable("PATH", "User"), "User")""");
        Console.WriteLine();
    }
}
