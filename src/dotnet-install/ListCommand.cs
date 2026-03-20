using DotnetInstall.Views;
using Markout;

static class ListCommand
{
    public static int Run(string installDir)
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
            Tools = entries.Select(e => new ToolRow(e.Name, e.LinkTarget)).ToList()
        };

        MarkoutSerializer.Serialize(view, Console.Out, new PlainTextFormatter(), ToolListViewContext.Default);

        return 0;
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
