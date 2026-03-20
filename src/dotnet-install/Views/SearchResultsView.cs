using Markout;

namespace DotnetInstall.Views;

[MarkoutSerializable]
public class SearchResultsView
{
    [MarkoutSection(Name = "Results")]
    public List<SearchRow>? Results { get; set; }
}

[MarkoutSerializable]
public record SearchRow(string Package, string Version, string Downloads, string? Description);

[MarkoutContext(typeof(SearchResultsView))]
public partial class SearchResultsViewContext : MarkoutSerializerContext
{
}
