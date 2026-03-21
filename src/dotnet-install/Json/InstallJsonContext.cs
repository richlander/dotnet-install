using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInstall.Json;

public record ToolListEntry(string Name, string? Version, string Type, string? Source);

public record ToolInfoEntry(
    string Name,
    string Type,
    long SizeBytes,
    string Location,
    string? LinkTarget,
    string? Modified,
    ToolSourceInfo? Source);

public record ToolSourceInfo(
    string? Type,
    string? Package,
    string? Version,
    string? Repository,
    string? Ref,
    string? Commit,
    string? Project);

public record SearchResultEntry(
    string Package,
    string Version,
    long TotalDownloads,
    string? Description,
    bool Verified);

public record OutdatedEntry(
    string Name,
    string Source,
    string Installed,
    string? Latest,
    string Status);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolListEntry))]
[JsonSerializable(typeof(ToolListEntry[]))]
[JsonSerializable(typeof(ToolInfoEntry))]
[JsonSerializable(typeof(SearchResultEntry))]
[JsonSerializable(typeof(SearchResultEntry[]))]
[JsonSerializable(typeof(OutdatedEntry))]
[JsonSerializable(typeof(OutdatedEntry[]))]
public partial class InstallJsonContext : JsonSerializerContext
{
}
