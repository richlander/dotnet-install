using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using NuGetFetch;

static class UpdateCommand
{
    public static async Task<int> RunAsync(string installDir, string[] args)
    {
        if (!Directory.Exists(installDir))
        {
            Console.WriteLine("No tools installed.");
            return 0;
        }

        // Determine which tools to update
        var toolNames = args.Where(a => !a.StartsWith('-')).ToList();
        bool updateAll = toolNames.Count == 0;

        var tools = DiscoverTools(installDir);

        if (tools.Count == 0)
        {
            Console.WriteLine("No tools with update metadata found.");
            return 0;
        }

        if (!updateAll)
        {
            tools = tools.Where(t => toolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            var missing = toolNames.Where(n => !tools.Any(t => t.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (string name in missing)
                Console.Error.WriteLine($"{name}: not found or no update metadata");
        }

        if (tools.Count == 0)
        {
            Console.WriteLine("Nothing to update.");
            return 0;
        }

        int failures = 0;

        foreach (var tool in tools)
        {
            // Prefer update channel over install source (e.g., installed from GitHub, updates from NuGet)
            var source = tool.Manifest.Update ?? tool.Manifest.Source!;

            switch (source.Type)
            {
                case "nuget":
                    if (await UpdateNuGetAsync(tool, source, installDir) != 0)
                        failures++;
                    break;

                case "github":
                case "git":
                    if (source.Pinned)
                    {
                        string refInfo = source.Ref ?? source.Commit?[..Math.Min(7, source.Commit.Length)] ?? "unknown";
                        Console.WriteLine($"{tool.Name}: pinned to {refInfo}, skipping (reinstall to change versions)");
                        break;
                    }
                    if (UpdateGitHub(tool, source, installDir) != 0)
                        failures++;
                    break;

                case "local":
                    if (UpdateLocal(tool, source, installDir) != 0)
                        failures++;
                    break;

                case "github-release":
                    if (await UpdateGitHubReleaseAsync(tool, source, installDir) != 0)
                        failures++;
                    break;

                default:
                    Console.Error.WriteLine($"{tool.Name}: unknown source type '{source.Type}'");
                    failures++;
                    break;
            }
        }

        return failures > 0 ? 1 : 0;
    }

    // ---- NuGet update ----

    static async Task<int> UpdateNuGetAsync(ToolInfo tool, InstallSource source, string installDir)
    {
        string packageName = source.Package!;

        // Use the best known installed version across source and update plan
        string installedVersion = GetInstalledVersion(tool) ?? "unknown";

        Console.Write($"{tool.Name} ({packageName} {installedVersion})... ");

        using var client = new HttpClient();
        var nuget = new NuGetClient(client);

        string? latestVersion = await nuget.GetLatestVersionAsync(packageName);
        if (latestVersion is null)
        {
            Console.WriteLine("package not found");
            return 1;
        }

        if (!IsNewer(latestVersion, installedVersion))
        {
            Console.WriteLine("up to date");
            return 0;
        }

        Console.WriteLine($"{installedVersion} -> {latestVersion}");
        int result = await Installer.InstallPackageAsync(
            $"{packageName}@{latestVersion}", installDir, quiet: true);

        // Preserve the update plan in metadata (InstallPackageAsync wrote source only)
        if (result == 0 && tool.Manifest.Update is not null)
        {
            string metaDir = Path.Combine(installDir, $"_{tool.Name}");
            var manifest = ToolMetadata.Read(metaDir);
            if (manifest is not null)
            {
                manifest.Update = tool.Manifest.Update;
                manifest.Update.Version = latestVersion;
                ToolMetadata.Write(metaDir, manifest);
            }
        }

        return result;
    }

    // ---- GitHub update ----

    static int UpdateGitHub(ToolInfo tool, InstallSource source, string installDir)
    {
        string repository = source.Repository!;
        string? gitRef = source.Ref;
        string? installedCommit = source.Commit;
        string shortCommit = installedCommit is not null && installedCommit.Length >= 7
            ? installedCommit[..7] : installedCommit ?? "unknown";

        Console.Write($"{tool.Name} ({repository} {shortCommit})... ");

        // Resolve cache paths
        int slashIndex = repository.IndexOf('/');
        string owner = repository[..slashIndex];
        string repo = repository[(slashIndex + 1)..];
        string cacheBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "git-tools");
        string repoDir = Path.Combine(cacheBase, owner, repo, "repo");

        if (!Directory.Exists(Path.Combine(repoDir, ".git")))
        {
            Console.WriteLine("not cached, reinstalling");
            string spec = gitRef is not null ? $"{repository}@{gitRef}" : repository;
            return GitSource.InstallFromGit(spec, installDir, source.Ssh, branch: gitRef, tag: null, rev: null, source.Project, quiet: true);
        }

        // Fetch latest
        if (Run("git", ["-C", repoDir, "fetch", "origin"]) != 0)
        {
            Console.WriteLine("fetch failed");
            return 1;
        }

        // Resolve the remote ref to compare
        string refToCheck = gitRef is not null ? $"origin/{gitRef}" : "HEAD";
        if (gitRef is null)
        {
            // Find default branch
            string? defaultRef = RunCapture("git", ["-C", repoDir, "symbolic-ref", "--short", "refs/remotes/origin/HEAD"]);
            if (defaultRef is not null)
                refToCheck = defaultRef.Trim();
        }

        string? latestCommit = RunCapture("git", ["-C", repoDir, "rev-parse", refToCheck])?.Trim();
        if (latestCommit is null)
        {
            Console.WriteLine("could not resolve ref");
            return 1;
        }

        if (string.Equals(latestCommit, installedCommit, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("up to date");
            return 0;
        }

        string shortLatest = latestCommit.Length >= 7 ? latestCommit[..7] : latestCommit;
        Console.WriteLine($"{shortCommit} -> {shortLatest}");

        string spec2 = gitRef is not null ? $"{repository}@{gitRef}" : repository;
        return GitSource.InstallFromGit(spec2, installDir, source.Ssh, branch: gitRef, tag: null, rev: null, source.Project, quiet: true);
    }

    // ---- Local update ----

    static int UpdateLocal(ToolInfo tool, InstallSource source, string installDir)
    {
        string projectPath = source.Project!;
        string? installedCommit = source.Commit;
        string shortCommit = installedCommit is not null && installedCommit.Length >= 7
            ? installedCommit[..7] : installedCommit ?? "unknown";

        Console.Write($"{tool.Name} (local {shortCommit})... ");

        if (!File.Exists(projectPath))
        {
            Console.WriteLine($"project not found: {projectPath}");
            return 1;
        }

        // Check git status in the project's directory
        string? projectDir = Path.GetDirectoryName(projectPath);
        if (projectDir is null)
        {
            Console.WriteLine("could not determine project directory");
            return 1;
        }

        string? currentCommit = RunCapture("git", ["-C", projectDir, "rev-parse", "HEAD"])?.Trim();

        if (currentCommit is not null &&
            string.Equals(currentCommit, installedCommit, StringComparison.OrdinalIgnoreCase))
        {
            // Same commit — check for uncommitted changes
            string? status = RunCapture("git", ["-C", projectDir, "status", "--porcelain"]);
            if (string.IsNullOrWhiteSpace(status))
            {
                Console.WriteLine("up to date");
                return 0;
            }
            Console.WriteLine("uncommitted changes, rebuilding");
        }
        else if (currentCommit is not null)
        {
            string shortCurrent = currentCommit.Length >= 7 ? currentCommit[..7] : currentCommit;
            Console.WriteLine($"{shortCommit} -> {shortCurrent}");
        }
        else
        {
            Console.WriteLine("not a git repo, rebuilding");
        }

        var newSource = new InstallSource
        {
            Type = "local",
            Project = projectPath,
            Commit = currentCommit
        };

        return Installer.Install(projectPath, installDir, newSource, quiet: true);
    }

    // ---- GitHub Release update ----

    static async Task<int> UpdateGitHubReleaseAsync(ToolInfo tool, InstallSource source, string installDir)
    {
        string repository = source.Repository!;
        string installedVersion = GetInstalledVersion(tool) ?? "unknown";

        Console.Write($"{tool.Name} ({repository} {installedVersion})... ");

        // Resolve latest version from GitHub Releases
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "dotnet-install");

        string latestUrl = $"https://github.com/{repository}/releases/latest";
        var request = new HttpRequestMessage(HttpMethod.Head, latestUrl);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        string? effectiveUrl = response.RequestMessage?.RequestUri?.ToString();
        if (effectiveUrl is null || !effectiveUrl.Contains("/tag/"))
        {
            Console.WriteLine("could not resolve latest version");
            return 1;
        }

        // Extract version from .../tag/v0.4.4
        int tagIndex = effectiveUrl.LastIndexOf("/v", StringComparison.Ordinal);
        if (tagIndex < 0)
        {
            Console.WriteLine("could not parse version from tag");
            return 1;
        }

        string latestVersion = effectiveUrl[(tagIndex + 2)..];

        if (!IsNewer(latestVersion, installedVersion))
        {
            Console.WriteLine("up to date");
            return 0;
        }

        Console.WriteLine($"{installedVersion} -> {latestVersion}");

        // Download and extract
        string rid = RuntimeInformation.RuntimeIdentifier;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string ext = isWindows ? "zip" : "tar.gz";
        string assetUrl = $"https://github.com/{repository}/releases/download/v{latestVersion}/{tool.Name}-{rid}.{ext}";

        string tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string archivePath = Path.Combine(tempDir, $"{tool.Name}.{ext}");

            using (var archiveStream = await client.GetStreamAsync(assetUrl))
            using (var fileStream = File.Create(archivePath))
            {
                await archiveStream.CopyToAsync(fileStream);
            }

            // Extract into a dedicated subdirectory so the archive itself isn't
            // counted when validating the payload is a single file.
            string extractDir = Path.Combine(tempDir, "extract");
            Directory.CreateDirectory(extractDir);

            if (!TryExtractReleaseArchive(archivePath, extractDir, isWindows))
                return 1;

            // Enforce the single-file contract, matching the install path: the
            // release asset must contain exactly one self-contained executable.
            // Reject multi-file or framework-dependent payloads.
            string binaryName = isWindows ? $"{tool.Name}.exe" : tool.Name;
            switch (ValidateReleasePayload(extractDir, binaryName, out string? extractedBinary))
            {
                case ReleasePayloadStatus.NotSingleFile:
                    Console.Error.WriteLine("  release asset is not a single-file executable; refusing to update");
                    return 1;
                case ReleasePayloadStatus.BinaryNotFound:
                    Console.Error.WriteLine("  binary not found in archive");
                    return 1;
            }

            // Replace the binary (rename-then-copy to handle self-update "text file busy")
            string targetBinary = Path.Combine(installDir, binaryName);
            string backupBinary = targetBinary + ".old";

            try { File.Delete(backupBinary); } catch { }

            if (File.Exists(targetBinary))
            {
                try
                {
                    File.Move(targetBinary, backupBinary);
                }
                catch (IOException)
                {
                    // On some systems even rename fails; proceed with copy attempt
                }
            }

            File.Copy(extractedBinary!, targetBinary, overwrite: true);

            if (!isWindows)
            {
                File.SetUnixFileMode(targetBinary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            try { File.Delete(backupBinary); } catch { }

            // Update metadata; purge any legacy managed payload/launcher so the
            // refreshed single-file binary is no longer classified as legacy.
            InstallLayout.RemoveLegacyLauncher(installDir, tool.Name);
            InstallLayout.ResetMetadataDirectory(installDir, tool.Name);
            string toolDir = InstallLayout.MetadataDirectory(installDir, tool.Name);
            ToolMetadata.Write(toolDir, new ToolManifest
            {
                Source = new InstallSource
                {
                    Type = "github-release",
                    Repository = repository,
                    Version = latestVersion
                }
            });

            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"  download failed: {ex.Message}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ---- Release payload validation ----

    internal enum ReleasePayloadStatus { Ok, NotSingleFile, BinaryNotFound }

    /// <summary>
    /// Extracts a downloaded release archive into <paramref name="extractDir"/>.
    /// The Unix tar path is walked entry by entry so we can (a) reject link entries
    /// outright — a single-file tool payload never needs them, and symlink/hardlink
    /// entries are the vector for extraction-time escape — and (b) contain every
    /// entry's resolved path within <paramref name="extractDir"/> ourselves, rather
    /// than trusting <c>TarFile.ExtractToDirectory</c>, whose physical symlink
    /// containment check is not present in all supported runtimes. This replaces
    /// shelling out to <c>tar -xzf</c>, which honors <c>../</c> entries (tar-slip).
    /// Returns false and writes a message to stderr on failure.
    /// </summary>
    internal static bool TryExtractReleaseArchive(string archivePath, string extractDir, bool isWindows)
    {
        string destRoot = Path.GetFullPath(extractDir);
        try
        {
            if (isWindows)
            {
                // ZipFile rejects entries that resolve outside the destination.
                ZipFile.ExtractToDirectory(archivePath, destRoot);
                return true;
            }

            using var archiveFile = File.OpenRead(archivePath);
            using var gzip = new GZipStream(archiveFile, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            for (TarEntry? entry = reader.GetNextEntry(); entry is not null; entry = reader.GetNextEntry())
            {
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                        break;
                    case TarEntryType.GlobalExtendedAttributes:
                    case TarEntryType.ExtendedAttributes:
                        // Metadata pseudo-entries; no filesystem object to create.
                        continue;
                    default:
                        // Symbolic/hard links, devices, fifos: never part of a valid
                        // single-file payload, and the escape vector we must refuse.
                        Console.Error.WriteLine($"  extract failed: unsupported archive entry '{entry.Name}' ({entry.EntryType})");
                        return false;
                }

                string fullTarget = Path.GetFullPath(Path.Combine(destRoot, entry.Name));
                if (!IsWithinDirectory(destRoot, fullTarget))
                {
                    Console.Error.WriteLine($"  extract failed: entry '{entry.Name}' escapes extraction directory");
                    return false;
                }

                if (entry.EntryType == TarEntryType.Directory)
                {
                    Directory.CreateDirectory(fullTarget);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
                    entry.ExtractToFile(fullTarget, overwrite: true);
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"  extract failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Returns true when <paramref name="candidateFull"/> is <paramref name="dirFull"/>
    /// itself or a path nested beneath it. Both arguments must be normalized full paths.</summary>
    static bool IsWithinDirectory(string dirFull, string candidateFull)
    {
        if (string.Equals(candidateFull, dirFull, StringComparison.Ordinal))
            return true;

        string dirWithSep = dirFull.EndsWith(Path.DirectorySeparatorChar)
            ? dirFull
            : dirFull + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(dirWithSep, StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates that an extracted github-release asset satisfies the single-file
    /// contract (exactly one self-contained executable, matching the install path)
    /// and locates the expected binary. <paramref name="extractDir"/> must contain
    /// only the extracted payload, not the downloaded archive.
    /// </summary>
    internal static ReleasePayloadStatus ValidateReleasePayload(string extractDir, string binaryName, out string? binaryPath)
    {
        binaryPath = null;
        if (!Installer.IsSingleFile(extractDir))
            return ReleasePayloadStatus.NotSingleFile;

        string candidate = Path.Combine(extractDir, binaryName);
        if (!File.Exists(candidate))
            return ReleasePayloadStatus.BinaryNotFound;

        binaryPath = candidate;
        return ReleasePayloadStatus.Ok;
    }

    // ---- Version helpers ----

    /// <summary>
    /// Returns the best known installed version by checking both Source and Update metadata.
    /// </summary>
    static string? GetInstalledVersion(ToolInfo tool)
    {
        string? sourceVer = tool.Manifest.Source?.Version;
        string? updateVer = tool.Manifest.Update?.Version;

        if (sourceVer is null) return updateVer;
        if (updateVer is null) return sourceVer;

        // Return the higher of the two
        if (TryParseVersion(sourceVer, out var sv) && TryParseVersion(updateVer, out var uv))
            return sv >= uv ? sourceVer : updateVer;

        return sourceVer;
    }

    /// <summary>
    /// Returns true only if latest is strictly newer than installed.
    /// Prevents downgrades when switching update channels.
    /// </summary>
    static bool IsNewer(string latest, string installed)
    {
        if (string.Equals(latest, installed, StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryParseVersion(latest, out var lv) && TryParseVersion(installed, out var iv))
            return lv > iv;

        // Can't parse — fall back to string inequality (assume newer)
        return true;
    }

    static bool TryParseVersion(string s, out Version version)
    {
        // Strip leading 'v' if present
        if (s.Length > 0 && s[0] == 'v')
            s = s[1..];

        return Version.TryParse(s, out version!);
    }

    // ---- Tool discovery ----

    record ToolInfo(string Name, ToolManifest Manifest);

    static List<ToolInfo> DiscoverTools(string installDir)
    {
        var tools = new List<ToolInfo>();

        foreach (string entry in Directory.GetDirectories(installDir))
        {
            string dirName = Path.GetFileName(entry);
            if (!dirName.StartsWith('_'))
                continue;

            string toolName = dirName[1..]; // strip leading underscore
            var manifest = ToolMetadata.Read(entry);
            if (manifest?.Source is not null)
                tools.Add(new ToolInfo(toolName, manifest));
        }

        return tools.OrderBy(t => t.Name).ToList();
    }

    // ---- Process helpers ----

    static int Run(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        p!.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    static string? RunCapture(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        string output = p!.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 ? output : null;
    }
}
