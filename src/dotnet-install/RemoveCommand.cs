static class RemoveCommand
{
    public static int Run(string installDir, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: dotnet-install remove <tool> [<tool>...]");
            return 1;
        }

        int exitCode = 0;

        foreach (string name in args)
        {
            if (name.StartsWith('-'))
                continue;

            // Prevent self-removal
            if (name.Equals("dotnet-install", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Skipping: dotnet-install (cannot remove self)");
                continue;
            }

            // On Windows `ls` shows the physical launcher name (e.g. foo.exe); accept
            // that form as well as the bare command name by normalizing to a logical
            // name used for both the launcher variants and the _<name>/ payload dir.
            string logicalName = LogicalName(name);

            // Find all launcher variants for the tool (a legacy install may leave a
            // <name>.cmd shim next to a newer <name>.exe; clean up both).
            string[] entryPaths = FindEntries(installDir, logicalName);

            if (entryPaths.Length == 0)
            {
                Console.Error.WriteLine($"Not found: {name}");
                exitCode = 1;
                continue;
            }

            string? target = null;

            // Also remove the _appname directory if it exists (legacy payload)
            string appDir = Path.Combine(installDir, $"_{logicalName}");
            if (Directory.Exists(appDir))
                Directory.Delete(appDir, true);

            foreach (string entryPath in entryPaths)
            {
                target ??= new FileInfo(entryPath).LinkTarget;
                File.Delete(entryPath);
            }

            if (target is not null)
                Console.WriteLine($"Removed: {name} (was -> {target})");
            else
                Console.WriteLine($"Removed: {name}");
        }

        return exitCode;
    }

    static string LogicalName(string name)
    {
        // On Windows the launcher carries a .exe (current) or legacy .cmd extension,
        // and `ls` prints that physical name; strip it to the logical command name so
        // `remove foo` and `remove foo.exe` behave the same. On Unix the command name
        // is used verbatim (dots are meaningful), so never strip.
        if (OperatingSystem.IsWindows() &&
            (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)))
        {
            return name[..^4];
        }

        return name;
    }

    static string[] FindEntries(string installDir, string name)
    {
        // On Windows a tool is installed as <name>.exe, possibly with a legacy
        // <name>.cmd shim beside it — those are the only launcher forms, so clean up
        // both. A bare extensionless top-level launcher is never produced on Windows,
        // so it is deliberately excluded (including it could conflate two distinct
        // tools whose names differ only by an .exe/.cmd suffix). On Unix the command
        // name is used verbatim, and <name>.exe / <name>.cmd would be unrelated tools,
        // so only the exact entry (or its dangling legacy symlink) is ours to remove.
        string[] candidates = OperatingSystem.IsWindows()
            ? [name + ".exe", name + ".cmd"]
            : [name];

        var found = new List<string>();
        foreach (string candidate in candidates)
        {
            string path = Path.Combine(installDir, candidate);
            // File.Exists follows symlinks, so a legacy launcher whose target is
            // already gone would be missed; match it as a link entry directly so
            // its stale _<name>/ payload can still be cleaned up.
            if (File.Exists(path) || IsSymlink(path))
                found.Add(path);
        }

        return found.ToArray();
    }

    static bool IsSymlink(string path)
    {
        try { return new FileInfo(path).LinkTarget is not null; }
        catch { return false; }
    }
}
