static class RemoveCommand
{
    public static int Run(string installDir, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: dotnet install remove <tool> [<tool>...]");
            return 1;
        }

        int exitCode = 0;

        foreach (string name in args)
        {
            if (name.StartsWith('-'))
                continue;

            // Find the tool entry (symlink, binary, or .cmd shim)
            string? entryPath = FindEntry(installDir, name);

            if (entryPath is null)
            {
                Console.Error.WriteLine($"  Not found: {name}");
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
                Console.WriteLine($"  Removed: {name} (was -> {target})");
            else
                Console.WriteLine($"  Removed: {name}");
        }

        return exitCode;
    }

    static string? FindEntry(string installDir, string name)
    {
        // Direct match
        string path = Path.Combine(installDir, name);
        if (File.Exists(path)) return path;

        // With .exe
        path = Path.Combine(installDir, name + ".exe");
        if (File.Exists(path)) return path;

        // With .cmd (Windows shim)
        path = Path.Combine(installDir, name + ".cmd");
        if (File.Exists(path)) return path;

        return null;
    }
}
