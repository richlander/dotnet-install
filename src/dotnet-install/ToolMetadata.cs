using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Metadata sidecar written alongside installed tools, tracking runtime dispatch
/// info and install provenance for updates.
///
/// The sidecar for a tool lives at <c>installDir/.tool.&lt;name&gt;.json</c> — a flat
/// dotfile next to the binary. Older installs kept it as <c>.tool.json</c> inside a
/// per-tool <c>_&lt;name&gt;/</c> directory; those are still read, and are migrated to
/// the flat form the next time the tool is installed or updated.
/// </summary>
static class ToolMetadata
{
    /// <summary>Legacy sidecar filename, inside <c>_&lt;name&gt;/</c>.</summary>
    internal const string FileName = ".tool.json";

    const string SidecarPrefix = ".tool.";
    const string SidecarSuffix = ".json";

    /// <summary>Sidecar path for a tool: <c>installDir/.tool.&lt;name&gt;.json</c>.</summary>
    internal static string SidecarPath(string installDir, string toolName) =>
        Path.Combine(installDir, SidecarPrefix + toolName + SidecarSuffix);

    /// <summary>
    /// The tool name a sidecar filename encodes, or null if it isn't a sidecar.
    /// </summary>
    internal static string? ToolNameFromSidecar(string fileName)
    {
        if (!fileName.StartsWith(SidecarPrefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(SidecarSuffix, StringComparison.Ordinal))
            return null;

        int length = fileName.Length - SidecarPrefix.Length - SidecarSuffix.Length;
        return length > 0 ? fileName.Substring(SidecarPrefix.Length, length) : null;
    }

    internal static string GetPath(string toolDir) =>
        Path.Combine(toolDir, FileName);

    /// <summary>
    /// Write a tool's sidecar, and clear the legacy <c>_&lt;name&gt;/</c> directory if
    /// it held nothing but the old sidecar. A directory with other content is left
    /// alone — that is stale managed payload, which the install path purges
    /// separately via <see cref="InstallLayout.ResetMetadataDirectory"/>.
    /// </summary>
    internal static void Write(string installDir, string toolName, ToolManifest manifest)
    {
        Directory.CreateDirectory(installDir);
        string json = JsonSerializer.Serialize(manifest, ToolManifestContext.Default.ToolManifest);
        File.WriteAllText(SidecarPath(installDir, toolName), json);

        string legacyDir = InstallLayout.MetadataDirectory(installDir, toolName);
        if (Directory.Exists(legacyDir) && HoldsOnlyLegacySidecar(legacyDir))
        {
            try
            {
                Directory.Delete(legacyDir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static bool HoldsOnlyLegacySidecar(string dir)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(dir))
        {
            if (!string.Equals(Path.GetFileName(path), FileName, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Read a tool's sidecar, preferring the flat form and falling back to the
    /// legacy <c>_&lt;name&gt;/.tool.json</c> so installs predating the move keep working.
    /// </summary>
    internal static ToolManifest? Read(string installDir, string toolName) =>
        ReadFile(SidecarPath(installDir, toolName))
        ?? ReadFile(GetPath(InstallLayout.MetadataDirectory(installDir, toolName)));

    /// <summary>Read a legacy sidecar from a <c>_&lt;name&gt;/</c> directory.</summary>
    internal static ToolManifest? ReadFromDirectory(string toolDir) =>
        ReadFile(GetPath(toolDir));

    /// <summary>
    /// Every tool with a sidecar in <paramref name="installDir"/>, flat form and
    /// legacy directories alike. Flat wins when a tool somehow has both.
    /// </summary>
    internal static List<(string Name, ToolManifest Manifest)> Discover(string installDir)
    {
        var found = new Dictionary<string, ToolManifest>(StringComparer.Ordinal);

        if (!Directory.Exists(installDir))
            return [];

        foreach (string dir in Directory.GetDirectories(installDir))
        {
            string dirName = Path.GetFileName(dir);
            if (!dirName.StartsWith('_') || dirName.Length < 2)
                continue;

            if (ReadFromDirectory(dir) is { } legacy)
                found[dirName[1..]] = legacy;
        }

        foreach (string file in Directory.GetFiles(installDir))
        {
            if (ToolNameFromSidecar(Path.GetFileName(file)) is not { } name)
                continue;

            if (ReadFile(file) is { } manifest)
                found[name] = manifest;
        }

        return found.Select(kv => (kv.Key, kv.Value)).OrderBy(t => t.Key).ToList();
    }

    static ToolManifest? ReadFile(string path)
    {
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

    /// <summary>Delete a tool's sidecar, both flat and legacy forms.</summary>
    internal static void Delete(string installDir, string toolName)
    {
        string sidecar = SidecarPath(installDir, toolName);
        if (File.Exists(sidecar))
            File.Delete(sidecar);
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
