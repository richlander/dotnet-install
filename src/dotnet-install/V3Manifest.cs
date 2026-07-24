using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The DotNetCliTool v3 package manifest, stored at <c>tools/manifest.json</c>
/// inside a NuGet package and discriminated by <c>"version": 3</c>.
///
/// A single type covers all three v3 package shapes:
/// <list type="bullet">
///   <item><b>Pointer (index)</b> — <see cref="Index"/> lists RID-specific packages;
///   the installer selects one by RID and redirects to it.</item>
///   <item><b>Pointer (bundle)</b> — <see cref="Bundle"/> lists other packages to
///   install together.</item>
///   <item><b>RID-specific</b> — <see cref="Descriptor"/> + <see cref="Commands"/>
///   describe the payload to place.</item>
/// </list>
/// </summary>
class V3Manifest
{
    internal const int SpecVersion = 3;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Pointer package: RID-specific packages to choose from.</summary>
    [JsonPropertyName("index")]
    public List<V3RidRef>? Index { get; set; }

    /// <summary>Pointer package: other packages to install together.</summary>
    [JsonPropertyName("bundle")]
    public List<V3BundleRef>? Bundle { get; set; }

    /// <summary>RID-specific package: which RID/id this payload is for.</summary>
    [JsonPropertyName("descriptor")]
    public V3RidRef? Descriptor { get; set; }

    /// <summary>RID-specific package: the commands this payload provides.</summary>
    [JsonPropertyName("commands")]
    public List<V3Command>? Commands { get; set; }

    /// <summary>
    /// Reads <c>tools/manifest.json</c> from an extracted package. Returns null if the
    /// package has no v3 manifest (a v1/v2 package) or the file cannot be parsed.
    /// </summary>
    internal static V3Manifest? TryRead(string extractPath)
    {
        string path = Path.Combine(extractPath, "tools", "manifest.json");
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), V3ManifestContext.Default.V3Manifest);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Selects the best RID-specific entry from an <see cref="Index"/> using the
    /// platform's RID fallback chain (exact RID → portable → <c>any</c>). Returns null
    /// if no listed RID is compatible with this platform.
    /// </summary>
    internal static V3RidRef? SelectRid(IReadOnlyList<V3RidRef> index, IReadOnlyList<string> ridFallbacks)
    {
        foreach (string rid in ridFallbacks)
        {
            foreach (var entry in index)
            {
                if (!string.IsNullOrEmpty(entry.Id)
                    && string.Equals(entry.Rid, rid, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }
        return null;
    }
}

/// <summary>A RID → package reference, with an optional runtime-version gate (for <c>any</c>).</summary>
class V3RidRef
{
    [JsonPropertyName("rid")]
    public string? Rid { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Runtime version required by this payload (two- or three-part). Only for the managed <c>any</c> fallback.</summary>
    [JsonPropertyName("runtimeVersion")]
    public string? RuntimeVersion { get; set; }
}

/// <summary>A bundle member: a package id and (optional) exact version.</summary>
class V3BundleRef
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>NuGet version, possibly an exact-match range like <c>[9.0.661903]</c>.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>A command a RID-specific payload provides.</summary>
class V3Command
{
    /// <summary>The executable (or, for the managed fallback, the .dll) to run.</summary>
    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    /// <summary>Absent for native BYOR payloads; <c>"dotnet"</c> for the managed <c>any</c> fallback.</summary>
    [JsonPropertyName("runner")]
    public string? Runner { get; set; }
}

[JsonSerializable(typeof(V3Manifest))]
partial class V3ManifestContext : JsonSerializerContext { }
