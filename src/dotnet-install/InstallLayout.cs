/// <summary>
/// Distinguishes current single-file installs from pre-redesign managed installs.
///
/// A current install places a real native executable at <c>installDir/&lt;name&gt;</c>
/// and writes only the ToolMetadata sidecar (<c>.tool.json</c>) into the metadata
/// directory <c>installDir/_&lt;name&gt;</c>. Before the single-file redesign, a managed
/// tool was installed as a symlink (or Windows <c>.cmd</c> shim) backed by a
/// <c>_&lt;name&gt;/</c> directory of DLLs and a runtimeconfig. Those layouts are no
/// longer produced, but existing ones are neither migrated nor cleaned up, so detect
/// them here to (a) label them honestly in <c>ls</c>/<c>info</c> instead of calling
/// them single-file and (b) drive cleanup on reinstall/remove.
/// </summary>
static class InstallLayout
{
    internal const string SingleFileType = "single-file";
    internal const string LegacyManagedType = "managed (legacy)";

    /// <summary>Metadata directory for a tool: <c>installDir/_&lt;name&gt;</c>.</summary>
    internal static string MetadataDirectory(string installDir, string toolName) =>
        Path.Combine(installDir, $"_{toolName}");

    /// <summary>
    /// True when the install at <paramref name="entry"/> is a leftover pre-redesign
    /// managed install: either the entry is a symlink (a legacy launcher — current
    /// installs are real copied binaries) or its metadata directory still holds
    /// managed payload beyond the <c>.tool.json</c> sidecar.
    /// </summary>
    internal static bool IsLegacyManaged(string installDir, string toolName, FileInfo entry)
    {
        if (entry.LinkTarget is not null)
            return true;

        string appDir = MetadataDirectory(installDir, toolName);
        if (!Directory.Exists(appDir))
            return false;

        foreach (string path in Directory.EnumerateFileSystemEntries(appDir))
        {
            if (!string.Equals(Path.GetFileName(path), ToolMetadata.FileName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Type label for <c>ls</c>/<c>info</c>.</summary>
    internal static string ClassifyType(string installDir, string toolName, FileInfo entry) =>
        IsLegacyManaged(installDir, toolName, entry) ? LegacyManagedType : SingleFileType;

    /// <summary>
    /// Reset a tool's metadata directory so a (re)install or update over a legacy
    /// managed layout leaves only fresh single-file metadata with no stale payload.
    /// Callers write the new <c>.tool.json</c> into it afterward.
    /// </summary>
    internal static void ResetMetadataDirectory(string installDir, string toolName)
    {
        string metaDir = MetadataDirectory(installDir, toolName);
        if (Directory.Exists(metaDir))
            Directory.Delete(metaDir, recursive: true);
        Directory.CreateDirectory(metaDir);
    }

    /// <summary>
    /// Remove a stale pre-redesign Windows <c>.cmd</c> launcher for a tool now being
    /// installed/updated as a single-file binary. The launcher delegated to payload
    /// in <c>_&lt;name&gt;/</c> that is being purged, so leaving it behind would strand
    /// a broken command on PATH (and a duplicate entry in <c>ls</c>).
    /// </summary>
    internal static void RemoveLegacyLauncher(string installDir, string toolName)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string cmdShim = Path.Combine(installDir, toolName + ".cmd");
        if (File.Exists(cmdShim))
            File.Delete(cmdShim);
    }
}
