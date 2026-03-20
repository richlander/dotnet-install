using System.Text.Json;
using DotnetInstall.Json;
using DotnetInstall.Views;
using Markout;
using Markout.Formatting;
using NuGetFetch;

static class SearchCommand
{
    public static async Task<int> RunAsync(string query, int take = 20, bool noHeader = false, string? columns = null, bool json = false)
    {
        using var client = new HttpClient();
        var search = new SearchService(client);

        var results = await search.SearchAsync(query, take);

        if (results.Count == 0)
        {
            if (json) Console.WriteLine("[]");
            else Console.WriteLine($"No packages found for '{query}'.");
            return 0;
        }

        if (json)
        {
            var jsonEntries = results
                .Select(r => new SearchResultEntry(r.Id, r.Version, r.TotalDownloads, r.Description, r.Verified))
                .ToArray();
            Console.WriteLine(JsonSerializer.Serialize(jsonEntries, InstallJsonContext.Default.SearchResultEntryArray));
            return 0;
        }

        var view = new SearchResultsView
        {
            Results = results.Select(r => new SearchRow(
                r.Id,
                r.Version,
                FormatHelper.FormatDownloads(r.TotalDownloads),
                FormatHelper.Truncate(r.Description, 60)
            )).ToList()
        };

        var writerOptions = new MarkoutWriterOptions();
        if (columns is not null)
        {
            var cols = columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            writerOptions.Projection = MarkoutProjection.WithColumns(cols);
        }

        MarkoutSerializer.Serialize(view, Console.Out, new OneLineFormatter(showHeader: !noHeader), SearchResultsViewContext.Default, writerOptions);

        return 0;
    }
}
