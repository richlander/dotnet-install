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

    /// <summary>
    /// Reads a sidecar, or returns null if it is missing or unreadable.
    /// </summary>
    /// <remarks>
    /// Unlike a repo manifest, an unreadable sidecar is tolerated: list, info, and
    /// update walk every installed tool, and one damaged file should not take the
    /// whole command down. Null is the safe answer — the tool reports an unknown
    /// source and update declines to touch it, rather than acting on a reading
    /// that may not be the one the file appears to give.
    /// </remarks>
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

    /// <summary>
    /// Reads a manifest, or returns null if there is none.
    /// </summary>
    /// <remarks>
    /// A file that exists but cannot be read throws rather than returning null.
    /// Collapsing the two meant a typo reported the file as missing, sending the
    /// user to look for something that was right there — and worse, callers treat
    /// "no manifest" as licence to auto-detect a project, so a broken manifest
    /// silently installed something other than what it described.
    /// </remarks>
    static ToolConfig? ReadFile(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ToolConfigContext.Default.ToolConfig)
                ?? throw new ManifestException($"{path} is empty.");
        }
        catch (JsonException e)
        {
            throw new ManifestException($"{path} is not valid JSON: {e.Message}");
        }
        catch (IOException e)
        {
            throw new ManifestException($"{path} could not be read: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            throw new ManifestException($"{path} could not be read: {e.Message}");
        }
    }
}

/// <summary>
/// A manifest exists but cannot be trusted to say what it appears to say.
/// Fatal by design: the alternative is acting on a guess.
/// </summary>
class ManifestException(string message) : Exception(message);

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
/// <c>name</c> is the command placed on PATH (Cargo's <c>name</c>), and the entry
/// names exactly one source to install it from.
///
/// <list type="bullet">
///   <item><c>project</c> — a repo-relative <c>.csproj</c> or file-based app to
///   build from this repo's source (Cargo's <c>path</c>).</item>
///   <item><c>package</c> — a NuGet package, optionally pinned with
///   <c>version</c>.</item>
///   <item><c>repository</c> — another git repo (<c>owner/repo</c> or a URL),
///   optionally pinned with <c>ref</c>.</item>
/// </list>
///
/// A manifest may mix all three, so a repo can advertise its own tools alongside
/// the third-party ones its toolset depends on. <c>name</c> is optional: for a
/// project it defaults to the assembly name, and for a package or repository the
/// source declares its own command name.
/// </summary>
class Tool
{
    /// <summary>Command name on PATH. Derived from the source when omitted.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Repo-relative project (or file-based app) to build.</summary>
    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Project { get; set; }

    /// <summary>NuGet package id to install.</summary>
    [JsonPropertyName("package")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Package { get; set; }

    /// <summary>Version for <see cref="Package"/>. Latest when omitted.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    /// <summary>Another git repo to build, with the ref it is taken from.</summary>
    [JsonPropertyName("repository")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RepositorySpec? Repository { get; set; }

    /// <summary>
    /// The sources this entry names. Exactly one is valid; the count is what
    /// distinguishes "nothing specified" from "ambiguous" in error reporting.
    /// </summary>
    internal string[] DeclaredSources()
    {
        var declared = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(Project)) declared.Add("project");
        if (!string.IsNullOrWhiteSpace(Package)) declared.Add("package");
        // Presence, not completeness: a repository missing its url is still a
        // repository the user asked for, and saying so beats "names no source".
        if (Repository is not null) declared.Add("repository");
        return [.. declared];
    }
}

/// <summary>
/// A git repo a manifest entry names:
///
/// <code>
/// "repository": { "url": "owner/repo", "branch": "main" }
/// </code>
///
/// Exactly one of <c>branch</c>, <c>tag</c>, or <c>rev</c> is required, matching
/// the <c>--branch</c>, <c>--tag</c>, and <c>--rev</c> options. The distinction is
/// not cosmetic: a branch is tracked and keeps updating, while a tag or commit
/// pins the install.
///
/// There is no shorthand that omits the ref. Silently following whatever the
/// default branch points at today is exactly what makes a manifest unauditable —
/// naming <c>branch</c> still floats, but it says so out loud.
/// </summary>
class RepositorySpec
{
    /// <summary><c>owner/repo</c> on GitHub, or a git URL.</summary>
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    /// <summary>Branch to track. Updatable — <c>update</c> follows it.</summary>
    [JsonPropertyName("branch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Branch { get; set; }

    /// <summary>Tag to install. Pinned.</summary>
    [JsonPropertyName("tag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tag { get; set; }

    /// <summary>Commit SHA to install. Pinned.</summary>
    [JsonPropertyName("rev")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rev { get; set; }

    /// <summary>
    /// The refs this spec names. Exactly one is valid, and zero means "track the
    /// default branch"; the count is what separates those from an ambiguous spec.
    /// </summary>
    internal string[] DeclaredRefs()
    {
        var declared = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(Branch)) declared.Add("branch");
        if (!string.IsNullOrWhiteSpace(Tag)) declared.Add("tag");
        if (!string.IsNullOrWhiteSpace(Rev)) declared.Add("rev");
        return [.. declared];
    }
}

[JsonSourceGenerationOptions(AllowDuplicateProperties = false)]
[JsonSerializable(typeof(ToolManifest))]
[JsonSerializable(typeof(ToolConfig))]
partial class ToolManifestContext : JsonSerializerContext { }

// Keep backward-compatible name; ToolConfig uses same context
[JsonSourceGenerationOptions(AllowDuplicateProperties = false)]
[JsonSerializable(typeof(ToolConfig))]
partial class ToolConfigContext : JsonSerializerContext { }
