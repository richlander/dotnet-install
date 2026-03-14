static class ShellHint
{
    /// <summary>
    /// After a successful install, check if the install directory is on PATH.
    /// If not, print shell-specific instructions.
    /// </summary>
    public static void PrintIfNeeded(string installDir)
    {
        installDir = Path.GetFullPath(installDir);

        if (IsOnPath(installDir))
            return;

        string displayDir = installDir.Replace(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");

        Console.WriteLine();
        Console.WriteLine($"  ⚠ {displayDir} is not in your PATH.");

        if (OperatingSystem.IsWindows())
        {
            PrintWindowsHint(displayDir, installDir);
        }
        else
        {
            PrintUnixHint(displayDir, installDir);
        }
    }

    static bool IsOnPath(string dir)
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

    static void PrintUnixHint(string displayDir, string absoluteDir)
    {
        // Detect shell from $SHELL
        string shell = Environment.GetEnvironmentVariable("SHELL") ?? "";
        string shellName = Path.GetFileName(shell);

        // Build the export line — use $HOME-relative path when possible, absolute otherwise
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string pathEntry = absoluteDir.StartsWith(home + "/")
            ? $"$HOME/{Path.GetRelativePath(home, absoluteDir)}"
            : absoluteDir;
        string exportLine = $"""export PATH="{pathEntry}:$PATH" """.Trim();

        string? rcFile = shellName switch
        {
            "zsh" => "~/.zshrc",
            "bash" => GetBashRcFile(),
            "fish" => null, // fish uses a different syntax
            _ => null
        };

        if (shellName == "fish")
        {
            string fishLine = $"fish_add_path {displayDir}";
            Console.WriteLine($"  Add it with:");
            Console.WriteLine();
            Console.WriteLine($"    echo '{fishLine}' >> ~/.config/fish/config.fish && {fishLine}");
        }
        else if (rcFile is not null)
        {
            Console.WriteLine($"  Add it with:");
            Console.WriteLine();
            Console.WriteLine($"    echo '{exportLine}' >> {rcFile} && source {rcFile}");
        }
        else
        {
            Console.WriteLine($"  Add it to your shell config:");
            Console.WriteLine();
            Console.WriteLine($"    {exportLine}");
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

    static string GetBashRcFile()
    {
        // On macOS, bash reads ~/.bash_profile for login shells (default Terminal behavior)
        // On Linux, bash reads ~/.bashrc for interactive non-login shells
        if (OperatingSystem.IsMacOS())
            return "~/.bash_profile";

        return "~/.bashrc";
    }
}
