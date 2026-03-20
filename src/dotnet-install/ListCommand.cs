using DotnetInstall.Views;
using Markout;
using Markout.Formatting;

static class ListCommand
{
    public static int Run(string installDir, bool noHeader = false)
    {
        if (!Directory.Exists(installDir))
        {
            Console.WriteLine("No tools installed.");
            return 0;
        }

        var entries = Directory.GetFileSystemEntries(installDir)
            .Select(p => new FileInfo(p))
            .Where(f => IsToolEntry(f))
            .OrderBy(f => f.Name)
            .ToList();

        if (entries.Count == 0)
        {
            Console.WriteLine("No tools installed.");
            return 0;
        }

        var view = new ToolListView
        {
            Tools = entries.Select(e => new ToolRow(e.Name, GetToolType(e, installDir))).ToList()
        };

        MarkoutSerializer.Serialize(view, Console.Out, new OneLineFormatter(showHeader: !noHeader), ToolListViewContext.Default);

        return 0;
    }

    static string GetToolType(FileInfo f, string installDir)
    {
        string toolName = Path.GetFileNameWithoutExtension(f.Name);

        // Symlink to dotnet-install = CoreCLR (busybox host dispatch)
        if (f.LinkTarget is not null)
        {
            string target = Path.GetFileName(f.LinkTarget);
            if (target.StartsWith("dotnet-install"))
                return "CoreCLR";
        }

        // Check the app directory for a managed entry point
        string appDir = Path.Combine(installDir, $"_{toolName}");
        if (Directory.Exists(appDir))
        {
            // Has a .dll = managed (self-contained CoreCLR or framework-dependent)
            if (File.Exists(Path.Combine(appDir, $"{toolName}.dll")))
                return "CoreCLR";

            // Native multi-file (no .dll entry point)
            return "NAOT";
        }

        // Direct executable, no app dir = NAOT single-file
        return "NAOT";
    }

    static bool IsToolEntry(FileInfo f)
    {
        // Skip _appname directories (multi-file tool storage)
        if (f.Name.StartsWith('_'))
            return false;

        // Symlinks (single-file or multi-file launchers)
        if (f.LinkTarget is not null)
            return true;

        // Executable files
        if (!OperatingSystem.IsWindows())
            return (f.UnixFileMode & UnixFileMode.UserExecute) != 0;

        // Windows: .exe or .cmd shims
        return f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || f.Extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
    }
}
