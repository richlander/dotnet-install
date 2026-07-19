// Vendored from richlander/dotnet-inspect @ 0599bfe
//   src/DotnetInspector.Services/DotnetToolSettingsData.cs
// Local modification: the Commands list carries ToolCommand records (Name + EntryPoint +
// Runner) instead of bare command-name strings, so dotnet-install can resolve a tool's
// executable and detect managed runners without re-walking the XML.
// If a shared library is published, replace this vendored copy with a package reference.

namespace DotnetInspector.Services;

/// <summary>
/// A RID-specific payload package referenced by a <c>DotnetToolSettings.xml</c> v2 manifest.
/// </summary>
public sealed record ToolRidPackage(string RuntimeIdentifier, string PackageId);

/// <summary>
/// A single <c>&lt;Command&gt;</c> entry from a <c>DotnetToolSettings.xml</c> manifest.
/// <paramref name="EntryPoint"/> names the file to launch (which can differ from
/// <paramref name="Name"/>, the command the user types); <paramref name="Runner"/> is
/// <c>"dotnet"</c> for managed tools and empty/absent for native single-file tools.
/// </summary>
public sealed record ToolCommand(string Name, string? EntryPoint, string? Runner);

/// <summary>
/// Parsed contents of a NuGet tool package's <c>DotnetToolSettings.xml</c> manifest.
/// </summary>
public sealed record DotnetToolSettingsData(
    string? Version,
    string? ToolFormat,
    bool IsRidSpecificPointerPackage,
    IReadOnlyList<ToolCommand>? Commands,
    IReadOnlyList<ToolRidPackage>? RuntimeIdentifierPackages);
