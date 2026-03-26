/// <summary>
/// Shell configuration detection — shared by ShellHint (print-only) and DoctorCommand (write).
/// </summary>
record ShellConfig(string ShellName, string? RcFile, string? RcFileAbsolute, string ExportLine, string EnvLine, string DisplayDir)
{
    public const string EnvVar = "DOTNET_TOOL_BIN";

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

        string envLine = shellName == "fish"
            ? $"set -gx {EnvVar} \"{pathEntry}\""
            : $"export {EnvVar}=\"{pathEntry}\"";

        string exportLine = shellName == "fish"
            ? $"fish_add_path ${EnvVar}"
            : $"""export PATH="${EnvVar}:$PATH" """.Trim();

        string? rcFile = shellName switch
        {
            "zsh" => "~/.zshrc",
            "bash" => GetBashRcFile(),
            "fish" => "~/.config/fish/config.fish",
            _ => OperatingSystem.IsWindows() ? null : "~/.profile"
        };

        string? rcFileAbsolute = rcFile?.Replace("~", home);

        return new ShellConfig(shellName, rcFile, rcFileAbsolute, exportLine, envLine, displayDir);
    }

    /// <summary>
    /// The lines to append to the rc file (shell-specific syntax).
    /// </summary>
    public string RcLine => $"{EnvLine}\n{ExportLine}";

    /// <summary>
    /// Check if the rc file already contains the PATH configuration.
    /// </summary>
    public bool RcFileContainsPath()
    {
        if (RcFileAbsolute is null || !File.Exists(RcFileAbsolute))
            return false;

        string content = File.ReadAllText(RcFileAbsolute);
        return content.Contains(EnvVar) ||
               content.Contains(DisplayDir.Replace("~", "$HOME")) ||
               content.Contains(DisplayDir);
    }

    /// <summary>
    /// Check if the install dir is configured in the rc file but not yet active in
    /// the current shell (e.g. ephemeral shells, CI containers, unsourced sessions).
    /// </summary>
    public static bool IsConfiguredButNotActive(string installDir)
    {
        if (OperatingSystem.IsWindows())
            return false;

        if (IsOnPath(installDir))
            return false;

        var config = Detect(installDir);
        return config.RcFileContainsPath();
    }

    static string GetBashRcFile()
    {
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
        Console.WriteLine($"{DoctorCommand.Warn} {config.DisplayDir} is not in your PATH.");

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
            Console.WriteLine($"Run doctor to configure your shell:");
            Console.WriteLine();
            Console.WriteLine($"  dotnet-install doctor --fix");
        }
        else
        {
            Console.WriteLine($"Add to your shell config:");
            Console.WriteLine();
            Console.WriteLine($"  {config.EnvLine}");
            Console.WriteLine($"  {config.ExportLine}");
        }

        Console.WriteLine();
    }

    static void PrintWindowsHint(string displayDir, string absoluteDir)
    {
        Console.WriteLine($"Run doctor to configure your PATH:");
        Console.WriteLine();
        Console.WriteLine($"  dotnet-install doctor --fix");
        Console.WriteLine();
    }
}
