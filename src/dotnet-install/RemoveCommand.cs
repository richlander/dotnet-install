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

            // Find all launcher variants for the tool (a legacy install may leave a
            // <name>.cmd shim next to a newer <name>.exe; clean up both).
            string[] entryPaths = FindEntries(installDir, name);

            if (entryPaths.Length == 0)
            {
                Console.Error.WriteLine($"Not found: {name}");
                exitCode = 1;
                continue;
            }

            string? target = null;

            // Also remove the _appname directory if it exists (legacy payload)
            string appDir = Path.Combine(installDir, $"_{name}");
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

    static string[] FindEntries(string installDir, string name)
    {
        // On Windows a tool may be installed as <name>.exe with a legacy <name>.cmd
        // shim beside it, so clean up every variant. On Unix the command name is used
        // verbatim, and <name>.exe / <name>.cmd would be unrelated tools — only the
        // exact entry (or its dangling legacy symlink) is ours to remove.
        string[] candidates = OperatingSystem.IsWindows()
            ? [name, name + ".exe", name + ".cmd"]
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
