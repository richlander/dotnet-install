using Markout;

namespace DotnetInstall.Views;

/// <summary>
/// View model for tool list output.
/// </summary>
[MarkoutSerializable]
public class ToolListView
{
    [MarkoutSection(Name = "Installed tools")]
    public List<ToolRow>? Tools { get; set; }
}

[MarkoutSerializable]
public record ToolRow(string Name, string? Target);

[MarkoutContext(typeof(ToolListView))]
public partial class ToolListViewContext : MarkoutSerializerContext
{
}
