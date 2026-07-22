using System.Runtime.InteropServices;

/// <summary>
/// Detects a same-named executable already reachable on the user's PATH — most
/// importantly one installed as a .NET SDK global tool (`dotnet tool install -g`).
/// dotnet-install places binaries in its own bin directory; a second copy of the
/// same command elsewhere on PATH is a shadowing hazard. Rather than silently create
/// that ambiguity, we refuse and ask the user to remove the other copy first.
///
/// This asks the filesystem where the executable is (does the file exist on PATH /
/// in the global-tools shim directory) instead of parsing `dotnet tool list` output.
/// </summary>
static class GlobalToolCheck
{
    public sealed record Conflict(string Path, bool IsSdkGlobalTool);

    /// <summary>
    /// Returns the first conflicting executable found on PATH (or in the .NET SDK
    /// global-tools directory), or null if the command is free to install into
    /// <paramref name="installDir"/>.
    /// </summary>
    public static Conflict? Find(string commandName, string installDir)
    {
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var dirs = pathVar
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        string? toolsDir = GlobalToolsDirectory();
        if (toolsDir is not null && !dirs.Any(d => PathEquals(d, toolsDir)))
            dirs.Add(toolsDir);

        return Resolve(dirs, installDir, toolsDir, ExecutableNames(commandName), File.Exists);
    }

    /// <summary>
    /// Pure resolution core: scans <paramref name="searchDirs"/> for any of
    /// <paramref name="fileNames"/>, skipping <paramref name="installDir"/> (our own
    /// target). A hit in <paramref name="globalToolsDir"/> is flagged as an SDK tool.
    /// </summary>
    internal static Conflict? Resolve(
        IReadOnlyList<string> searchDirs,
        string installDir,
        string? globalToolsDir,
        IReadOnlyList<string> fileNames,
        Func<string, bool> fileExists)
    {
        foreach (string dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir))
                continue;

            // Our own install directory is not a conflict — that's where we're going.
            if (PathEquals(dir, installDir))
                continue;

            foreach (string fileName in fileNames)
            {
                string full = Path.Combine(dir, fileName);
                if (fileExists(full))
                {
                    bool isSdkTool = globalToolsDir is not null && PathEquals(dir, globalToolsDir);
                    return new Conflict(full, isSdkTool);
                }
            }
        }

        return null;
    }

    /// <summary>Prints a conflict error and returns the exit code (1) to propagate.</summary>
    public static int Report(string commandName, Conflict conflict)
    {
        string tag = conflict.IsSdkGlobalTool ? "  (.NET SDK tool)" : "";
        Console.Error.WriteLine($"error: a '{commandName}' command is already on your PATH:");
        Console.Error.WriteLine($"  {conflict.Path}{tag}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Installing it with dotnet-install would put a second copy on your PATH.");

        if (conflict.IsSdkGlobalTool)
        {
            Console.Error.WriteLine("Uninstall the .NET SDK tool first, then re-run this command:");
            Console.Error.WriteLine($"  dotnet tool uninstall -g {commandName}");
        }
        else
        {
            Console.Error.WriteLine("Remove the existing executable first, then re-run this command.");
        }

        return 1;
    }

    /// <summary>Candidate executable file names for a command on the current OS.</summary>
    internal static IReadOnlyList<string> ExecutableNames(string commandName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new[] { $"{commandName}.exe", commandName };
        return new[] { commandName };
    }

    /// <summary>
    /// The .NET SDK global-tools shim directory (honours DOTNET_CLI_HOME, else the
    /// user profile), or null if it cannot be determined.
    /// </summary>
    internal static string? GlobalToolsDirectory()
    {
        string? home = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME");
        if (string.IsNullOrEmpty(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return null;
        return Path.Combine(home, ".dotnet", "tools");
    }

    static bool PathEquals(string a, string b)
    {
        static string Norm(string p)
        {
            try { p = Path.GetFullPath(p); } catch { /* keep as-is */ }
            return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return string.Equals(Norm(a), Norm(b), comparison);
    }
}
