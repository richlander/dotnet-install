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

            // Find the tool entry (symlink, binary, or .cmd shim)
            string? entryPath = FindEntry(installDir, name);

            if (entryPath is null)
            {
                Console.Error.WriteLine($"Not found: {name}");
                exitCode = 1;
                continue;
            }

            var info = new FileInfo(entryPath);
            string? target = info.LinkTarget;

            // Also remove the _appname directory if it exists (multi-file installs)
            string appDir = Path.Combine(installDir, $"_{name}");
            if (Directory.Exists(appDir))
                Directory.Delete(appDir, true);

            File.Delete(entryPath);

            if (target is not null)
                Console.WriteLine($"Removed: {name} (was -> {target})");
            else
                Console.WriteLine($"Removed: {name}");
        }

        return exitCode;
    }

    static string? FindEntry(string installDir, string name)
    {
        foreach (string candidate in new[] { name, name + ".exe", name + ".cmd" })
        {
            string path = Path.Combine(installDir, candidate);
            // File.Exists follows symlinks, so a legacy launcher whose target is
            // already gone would be missed; match it as a link entry directly so
            // its stale _<name>/ payload can still be cleaned up.
            if (File.Exists(path) || IsSymlink(path))
                return path;
        }

        return null;
    }

    static bool IsSymlink(string path)
    {
        try { return new FileInfo(path).LinkTarget is not null; }
        catch { return false; }
    }
}
