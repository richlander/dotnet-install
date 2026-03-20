using System.Text.Json;
using DotnetInstall.Json;
using DotnetInstall.Views;
using Markout;
using Markout.Formatting;
using NuGetFetch;

static class OutdatedCommand
{
    public static async Task<int> RunAsync(string installDir, bool noHeader = false, string? columns = null, bool json = false)
    {
        if (!Directory.Exists(installDir))
        {
            Console.WriteLine("No tools installed.");
            return 0;
        }

        var tools = DiscoverTools(installDir);
        if (tools.Count == 0)
        {
            Console.WriteLine("No tools with update metadata found.");
            return 0;
        }

        using var client = new HttpClient();
        var nuget = new NuGetClient(client);

        var rows = new List<OutdatedRow>();

        foreach (var tool in tools)
        {
            var source = tool.Manifest.Source!;
            string? installed = null;
            string? latest = null;

            if (source.Type == "nuget" && source.Package is not null)
            {
                installed = source.Version ?? "unknown";
                latest = await nuget.GetLatestVersionAsync(source.Package);
            }
            else if (source.Type == "github" && source.Repository is not null)
            {
                installed = source.Commit is not null && source.Commit.Length >= 7
                    ? source.Commit[..7] : source.Commit ?? "unknown";
                latest = "—"; // Can't easily check remote commit
            }
            else if (source.Type == "local")
            {
                installed = source.Commit is not null && source.Commit.Length >= 7
                    ? source.Commit[..7] : source.Commit ?? "unknown";
                latest = "—";
            }

            bool upToDate = string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase);
            string status = latest == "—" ? "local" : upToDate ? "✓" : "update";

            rows.Add(new OutdatedRow(tool.Name, source.Type, installed ?? "unknown", latest ?? "unknown", status));
        }

        if (json)
        {
            var jsonEntries = rows
                .Select(r => new OutdatedEntry(r.Name, r.Source, r.Installed, r.Latest, r.Status))
                .ToArray();
            Console.WriteLine(JsonSerializer.Serialize(jsonEntries, InstallJsonContext.Default.OutdatedEntryArray));
            return 0;
        }

        var view = new OutdatedView
        {
            Tools = rows
        };

        var writerOptions = new MarkoutWriterOptions();
        if (columns is not null)
        {
            var cols = columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            writerOptions.Projection = MarkoutProjection.WithColumns(cols);
        }

        MarkoutSerializer.Serialize(view, Console.Out, new OneLineFormatter(showHeader: !noHeader), OutdatedViewContext.Default, writerOptions);

        return 0;
    }

    record ToolInfo(string Name, ToolManifest Manifest);

    static List<ToolInfo> DiscoverTools(string installDir)
    {
        var tools = new List<ToolInfo>();

        foreach (string entry in Directory.GetDirectories(installDir))
        {
            string dirName = Path.GetFileName(entry);
            if (!dirName.StartsWith('_'))
                continue;

            string toolName = dirName[1..];
            var manifest = ToolMetadata.Read(entry);
            if (manifest?.Source is not null)
                tools.Add(new ToolInfo(toolName, manifest));
        }

        return tools.OrderBy(t => t.Name).ToList();
    }
}
