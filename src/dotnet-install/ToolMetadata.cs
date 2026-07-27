using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Metadata sidecar (.tool.json) written alongside installed tools.
/// Tracks runtime dispatch info and install provenance for updates.
/// </summary>
static class ToolMetadata
{
    internal const string FileName = ".tool.json";

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
    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstallSource? Source { get; set; }

    /// <summary>
    /// Preferred update channel, overrides Source for updates.
    /// Set by repo config (.dotnet-install.json) or install scripts.
    /// </summary>
    [JsonPropertyName("update")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstallSource? Update { get; set; }
}

/// <summary>
/// Tracks how a tool was installed so it can be updated later.
/// Uses a flat structure with nullable fields per source type for AOT compatibility.
/// </summary>
class InstallSource
{
    /// <summary>"nuget", "github", "local", or "github-release"</summary>
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

    /// <summary>Whether this install is pinned (tag, @ref, commit SHA — not updatable).</summary>
    [JsonPropertyName("pinned")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Pinned { get; set; }
}

/// <summary>
/// The <c>.dotnet-install.json</c> config schema. It appears in two places,
/// with the same filename and schema:
///
/// <list type="bullet">
///   <item><b>Colocated</b> — sitting in a directory the tool is pointed at
///   directly (a project directory / local path).</item>
///   <item><b>Repo</b> — at <c>&lt;repo&gt;/.dotnet-install/.dotnet-install.json</c>,
///   read when installing via the repo gesture (<c>--github</c>/<c>--repo</c> or a
///   bare <c>.</c>). The repo root itself is never scanned — only the
///   <c>.dotnet-install/</c> directory.</item>
/// </list>
///
/// The toolset is described by the <c>tools</c> array (each entry a command
/// <c>name</c> plus, in source scenarios, a repo-relative <c>project</c>); one
/// entry is a single tool, several form a bundle. The legacy <c>exe</c> /
/// <c>project</c> / <c>bundle</c> fields remain readable and are normalized by
/// <see cref="GetTools"/>. The shape mirrors the DotNetCliTool v3 manifest, so a
/// repo can advertise the same toolset it publishes as a v3 bundle package.
/// </summary>
class ToolConfig
{
    internal const string FileName = ".dotnet-install.json";

    /// <summary>Well-known repo directory holding the advertise manifest.</summary>
    internal const string RepoDirName = ".dotnet-install";

    /// <summary>DotNetCliTool manifest version (3 for the v3 spec). Optional.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Version { get; set; }

    /// <summary>Display name for the advertised toolset. Optional.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>The executable/command name this tool produces.</summary>
    [JsonPropertyName("exe")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Exe { get; set; }

    /// <summary>Repo-relative project to install (single-tool repos).</summary>
    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Project { get; set; }

    /// <summary>Preferred update channel (e.g., NuGet package).</summary>
    [JsonPropertyName("update")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstallSource? Update { get; set; }

    /// <summary>
    /// Toolset the repo advertises. When present and installing from the repo
    /// root, every listed project is built and installed together.
    /// </summary>
    [JsonPropertyName("bundle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BundleEntry>? Bundle { get; set; }

    /// <summary>
    /// The tools this manifest describes. Each entry names a command (<c>name</c>)
    /// and, in source scenarios, the repo-relative <c>project</c> to build. One
    /// entry is a single-tool repo; several entries form a bundle. Both fields are
    /// optional: <c>project</c> is absent once a tool is a prebuilt package, and
    /// <c>name</c> is derived from the project's assembly name when omitted.
    /// </summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Tool>? Tools { get; set; }

    /// <summary>
    /// The effective source-build tool list, normalizing the legacy <c>exe</c> /
    /// <c>project</c> / <c>bundle</c> fields into the unified <c>tools</c> shape.
    /// A repo is "advertised" when this is non-empty. A single <c>tools</c> entry
    /// with no <c>project</c> is resolved by auto-detecting the repo's sole
    /// executable project (Go-style); a bundle of several requires each to name a
    /// <c>project</c>.
    /// </summary>
    internal List<Tool> GetTools()
    {
        if (Tools is { Count: > 0 })
            return Tools;

        var list = new List<Tool>();

        if (Bundle is { Count: > 0 })
        {
            foreach (var entry in Bundle)
                list.Add(new Tool { Project = entry.Project });
            return list;
        }

        if (Project is not null)
            list.Add(new Tool { Name = Exe, Project = Project });

        // `exe` alone names a command but no buildable source, so it does not
        // advertise a source-build tool (returns an empty list).
        return list;
    }

    /// <summary>
    /// Reads a colocated <c>.dotnet-install.json</c> from a directory. Returns null if absent.
    /// </summary>
    internal static ToolConfig? Read(string directory) =>
        ReadFile(Path.Combine(directory, FileName));

    /// <summary>
    /// Reads a repo's advertise manifest from <c>&lt;repoRoot&gt;/.dotnet-install/.dotnet-install.json</c>.
    /// The repo root itself is never scanned. Returns null if absent.
    /// </summary>
    internal static ToolConfig? ReadFromRepo(string repoRoot) =>
        ReadFile(Path.Combine(repoRoot, RepoDirName, FileName));

    static ToolConfig? ReadFile(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ToolConfigContext.Default.ToolConfig);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// A single entry in a repo's advertised tool bundle. Points at a
/// repo-relative project (or file-based app) to build and install.
/// </summary>
class BundleEntry
{
    /// <summary>Repo-relative path to the project or file-based app to install.</summary>
    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Project { get; set; }
}

/// <summary>
/// A single tool a manifest describes. Modeled on Cargo's <c>[[bin]]</c> target:
/// <c>name</c> is the command placed on PATH (Cargo's <c>name</c>), and
/// <c>project</c> is the repo-relative source to build (Cargo's <c>path</c>).
/// Both are optional: <c>project</c> is present only in source scenarios, and
/// <c>name</c> is derived from the project's assembly name when omitted.
/// </summary>
class Tool
{
    /// <summary>Command name on PATH. Derived from the assembly name when omitted.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Repo-relative project (or file-based app) to build. Source-only.</summary>
    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Project { get; set; }
}

[JsonSerializable(typeof(ToolManifest))]
[JsonSerializable(typeof(ToolConfig))]
partial class ToolManifestContext : JsonSerializerContext { }

// Keep backward-compatible name; ToolConfig uses same context
[JsonSerializable(typeof(ToolConfig))]
partial class ToolConfigContext : JsonSerializerContext { }
