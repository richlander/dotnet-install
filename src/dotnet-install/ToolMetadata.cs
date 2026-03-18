using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Metadata sidecar (.tool.json) written alongside installed tools.
/// Tracks runtime dispatch info and install provenance for updates.
/// </summary>
static class ToolMetadata
{
    const string FileName = ".tool.json";

    internal static string GetPath(string toolDir) =>
        Path.Combine(toolDir, FileName);

    internal static void Write(string toolDir, ToolManifest manifest)
    {
        string path = GetPath(toolDir);
        string json = JsonSerializer.Serialize(manifest, ToolManifestContext.Default.ToolManifest);
        File.WriteAllText(path, json);
    }

    internal static ToolManifest? Read(string toolDir)
    {
        string path = GetPath(toolDir);
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ToolManifestContext.Default.ToolManifest);
        }
        catch
        {
            return null;
        }
    }
}

class ToolManifest
{
    [JsonPropertyName("entryPoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntryPoint { get; set; }

    [JsonPropertyName("rollForward")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RollForward { get; set; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstallSource? Source { get; set; }
}

/// <summary>
/// Tracks how a tool was installed so it can be updated later.
/// Uses a flat structure with nullable fields per source type for AOT compatibility.
/// </summary>
class InstallSource
{
    /// <summary>"nuget", "github", or "local"</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    // ---- NuGet ----

    [JsonPropertyName("package")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Package { get; set; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    // ---- GitHub ----

    [JsonPropertyName("repository")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Repository { get; set; }

    [JsonPropertyName("ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ref { get; set; }

    [JsonPropertyName("ssh")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Ssh { get; set; }

    // ---- GitHub + Local ----

    [JsonPropertyName("commit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Commit { get; set; }

    // ---- Local ----

    /// <summary>Absolute project path (local) or relative subpath (GitHub --project)</summary>
    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Project { get; set; }
}

[JsonSerializable(typeof(ToolManifest))]
partial class ToolManifestContext : JsonSerializerContext { }
