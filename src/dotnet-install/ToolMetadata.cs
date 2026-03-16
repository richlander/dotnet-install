using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Metadata sidecar (.tool.json) written alongside managed NuGet tools.
/// Read at dispatch time by the runtime host to determine entry point
/// and roll-forward policy.
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
    public string EntryPoint { get; set; } = "";

    [JsonPropertyName("rollForward")]
    public bool RollForward { get; set; }
}

[JsonSerializable(typeof(ToolManifest))]
partial class ToolManifestContext : JsonSerializerContext { }
