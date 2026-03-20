using Markout;

namespace DotnetInstall.Views;

[MarkoutSerializable]
public class OutdatedView
{
    [MarkoutSection(Name = "Tool versions")]
    public List<OutdatedRow>? Tools { get; set; }
}

[MarkoutSerializable]
public record OutdatedRow(string Name, string Source, string Installed, string Latest, string Status);

[MarkoutContext(typeof(OutdatedView))]
public partial class OutdatedViewContext : MarkoutSerializerContext
{
}
