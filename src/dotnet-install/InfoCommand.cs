using System.Text.Json;
using DotnetInstall.Json;
using DotnetInstall.Views;
using Markout;
using Markout.Formatting;

static class InfoCommand
{
    public static int Run(string installDir, string toolName, bool json = false)
    {
        string? entryPath = FindEntry(installDir, toolName);
        if (entryPath is null)
        {
            Console.Error.WriteLine($"error: '{toolName}' is not installed");
            return 1;
        }

        var info = new FileInfo(entryPath);
        string appDir = Path.Combine(installDir, $"_{toolName}");
        var manifest = Directory.Exists(appDir) ? ToolMetadata.Read(appDir) : null;

        // Determine type
        string type;
        if (info.LinkTarget is not null)
        {
            string target = Path.GetFileName(info.LinkTarget);
            type = target.StartsWith("dotnet-install") ? "CoreCLR (managed)" : "CoreCLR (self-contained)";
            if (Directory.Exists(appDir) && File.Exists(Path.Combine(appDir, $"{toolName}.dll")))
                type = target.StartsWith("dotnet-install") ? "CoreCLR (managed)" : "CoreCLR (self-contained)";
            else if (Directory.Exists(appDir))
                type = "NAOT (multi-file)";
        }
        else
        {
            type = Directory.Exists(appDir) ? "NAOT (multi-file)" : "NAOT (single-file)";
            if (Directory.Exists(appDir) && File.Exists(Path.Combine(appDir, $"{toolName}.dll")))
                type = "CoreCLR (self-contained)";
        }

        // Calculate size
        long totalSize = info.Length;
        if (Directory.Exists(appDir))
        {
            foreach (var f in Directory.GetFiles(appDir, "*", SearchOption.AllDirectories))
                totalSize += new FileInfo(f).Length;
        }

        // Build output
        if (json)
        {
            var sourceInfo = manifest?.Source is not null ? new ToolSourceInfo(
                manifest.Source.Type,
                manifest.Source.Package,
                manifest.Source.Version,
                manifest.Source.Repository,
                manifest.Source.Ref,
                manifest.Source.Commit,
                manifest.Source.Project) : null;

            var entry = new ToolInfoEntry(
                toolName, type, totalSize, entryPath,
                info.LinkTarget,
                info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                sourceInfo);
            Console.WriteLine(JsonSerializer.Serialize(entry, InstallJsonContext.Default.ToolInfoEntry));
            return 0;
        }

        Console.WriteLine($"{toolName}");
        Console.WriteLine();
        Console.WriteLine($"Type:      {type}");
        Console.WriteLine($"Size:      {FormatHelper.FormatSize(totalSize)}");
        Console.WriteLine($"Location:  {entryPath}");

        if (info.LinkTarget is not null)
            Console.WriteLine($"Target:    {info.LinkTarget}");

        Console.WriteLine($"Modified:  {info.LastWriteTime:yyyy-MM-dd HH:mm}");

        if (manifest?.Source is not null)
        {
            var src = manifest.Source;
            Console.WriteLine();
            Console.WriteLine($"Source:    {src.Type}");

            if (src.Package is not null)
                Console.WriteLine($"Package:   {src.Package}");
            if (src.Version is not null)
                Console.WriteLine($"Version:   {src.Version}");
            if (src.Repository is not null)
                Console.WriteLine($"Repo:      {src.Repository}");
            if (src.Ref is not null)
                Console.WriteLine($"Ref:       {src.Ref}");
            if (src.Commit is not null)
                Console.WriteLine($"Commit:    {src.Commit[..Math.Min(7, src.Commit.Length)]}");
            if (src.Project is not null)
                Console.WriteLine($"Project:   {src.Project}");
        }

        if (manifest is not null)
        {
            if (manifest.RollForward)
                Console.WriteLine($"Roll-fwd:  enabled");
            if (manifest.EntryPoint is not null)
                Console.WriteLine($"Entry:     {manifest.EntryPoint}");
        }

        return 0;
    }

    static string? FindEntry(string installDir, string name)
    {
        string path = Path.Combine(installDir, name);
        if (File.Exists(path)) return path;

        path = Path.Combine(installDir, name + ".exe");
        if (File.Exists(path)) return path;

        path = Path.Combine(installDir, name + ".cmd");
        if (File.Exists(path)) return path;

        return null;
    }
}
