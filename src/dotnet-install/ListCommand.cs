using System.Text.Json;
using DotnetInstall.Json;
using DotnetInstall.Views;
using Markout;
using Markout.Formatting;

static class ListCommand
{
    public static int Run(string installDir, bool noHeader = false, string? columns = null, bool json = false)
    {
        if (!Directory.Exists(installDir))
        {
            if (json) Console.WriteLine("[]");
            else Console.WriteLine("No tools installed.");
            return 0;
        }

        var entries = Directory.GetFileSystemEntries(installDir)
            .Select(p => new FileInfo(p))
            .Where(f => IsToolEntry(f))
            .OrderBy(f => f.Name)
            .ToList();

        if (entries.Count == 0)
        {
            if (json) Console.WriteLine("[]");
            else Console.WriteLine("No tools installed.");
            return 0;
        }

        if (json)
        {
            var jsonEntries = entries
                .Select(e => {
                    var m = ReadManifest(e, installDir);
                    return new ToolListEntry(e.Name, GetDisplayVersion(m), GetToolType(e, installDir), m?.Source?.Type);
                })
                .ToArray();
            Console.WriteLine(JsonSerializer.Serialize(jsonEntries, InstallJsonContext.Default.ToolListEntryArray));
            return 0;
        }

        var view = new ToolListView
        {
            Tools = entries.Select(e => {
                var m = ReadManifest(e, installDir);
                return new ToolRow(e.Name, GetDisplayVersion(m) ?? "", GetToolType(e, installDir), m?.Source?.Type ?? "");
            }).ToList()
        };

        var writerOptions = new MarkoutWriterOptions();

        if (columns is not null)
        {
            var cols = columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            writerOptions.Projection = MarkoutProjection.WithColumns(cols);
        }

        MarkoutSerializer.Serialize(view, Console.Out, new OneLineFormatter(showHeader: !noHeader), ToolListViewContext.Default, writerOptions);

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

    static ToolManifest? ReadManifest(FileInfo f, string installDir)
    {
        string toolName = Path.GetFileNameWithoutExtension(f.Name);
        string appDir = Path.Combine(installDir, $"_{toolName}");
        return ToolMetadata.Read(appDir);
    }

    /// <summary>
    /// Version for display: package version if available, short commit SHA for git sources.
    /// </summary>
    static string? GetDisplayVersion(ToolManifest? manifest)
    {
        if (manifest?.Source is null) return null;

        if (manifest.Source.Version is not null)
            return manifest.Source.Version;

        // Git-sourced tools: show short commit
        if (manifest.Source.Commit is { Length: >= 7 } commit)
            return commit[..7];

        return null;
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
