using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using NuGetFetch;

static class Installer
{
    public static string DefaultInstallDir
    {
        get
        {
            string? envDir = Environment.GetEnvironmentVariable(ShellConfig.EnvVar);
            if (!string.IsNullOrEmpty(envDir))
                return envDir;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "bin");
        }
    }

    public static string LocalBinDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    public static int Install(string projectFile, string installDir, InstallSource? source = null, bool requireSourceLink = false, bool quiet = false, InstallSource? update = null)
    {
        // 1. Evaluate the project to read properties before building
        var info = EvaluateProject(projectFile);

        if (!IsExecutable(info.OutputType))
        {
            Console.Error.WriteLine($"error: not an executable project (OutputType={info.OutputType})");
            return 1;
        }

        string appName = info.AssemblyName;

        // This tool installs only single-file executables. A project must opt into
        // Native AOT or self-contained single-file publishing; anything else is a
        // managed/multi-file tool and belongs to `dotnet tool install`.
        if (!info.IsNativeAot && !(info.IsSingleFile && info.IsSelfContained))
        {
            Console.Error.WriteLine($"error: '{appName}' is not a single-file executable project.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("dotnet-install only installs single-file native executables. Set one of:");
            Console.Error.WriteLine("  <PublishAot>true</PublishAot>");
            Console.Error.WriteLine("  <PublishSingleFile>true</PublishSingleFile> with <SelfContained>true</SelfContained>");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Or install it as a managed .NET tool with the SDK:");
            Console.Error.WriteLine("  dotnet tool install -g <package>");
            return 1;
        }

        string mode = info.IsNativeAot ? "Native AOT" : "single-file";

        // Pre-flight: warn if the project's TFM may not be buildable
        if (info.TargetFramework is not null)
            CheckSdkCompatibility(info.TargetFramework);

        if (!quiet)
        {
            Console.WriteLine($"Installing {appName} to {installDir}");
            Console.WriteLine($"Publishing ({mode}, Release)...");
        }

        // 2. Execute the Publish target via MSBuild API
        string tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Path.GetRandomFileName()}");

        try
        {
            if (!Publish(projectFile, tempDir))
            {
                Console.Error.WriteLine("error: publish failed");
                return 1;
            }

            // 3. SourceLink verification (before placement)
            if (requireSourceLink && !SourceLinkCheck.Verify(tempDir))
            {
                Console.Error.WriteLine("error: --require-sourcelink specified but SourceLink verification failed");
                return 1;
            }

            // 4. Locate executable in publish output
            string execName = OperatingSystem.IsWindows() ? $"{appName}.exe" : appName;
            string execPath = Path.Combine(tempDir, execName);

            if (!File.Exists(execPath))
            {
                Console.Error.WriteLine($"error: '{execName}' not found in publish output");
                return 1;
            }

            // 5. Verify the publish produced a single file and place it
            if (!IsSingleFile(tempDir))
            {
                Console.Error.WriteLine($"error: publishing '{appName}' produced multiple files, not a single executable.");
                Console.Error.WriteLine("Enable <PublishAot>true</PublishAot> or <PublishSingleFile>true</PublishSingleFile>,");
                Console.Error.WriteLine("or install it as a managed .NET tool: dotnet tool install -g <package>");
                return 1;
            }

            Directory.CreateDirectory(installDir);
            PlaceSingleFile(execPath, installDir, execName);

            // Write install metadata (for update tracking)
            if (source is not null)
            {
                string metaDir = Path.Combine(installDir, $"_{appName}");
                Directory.CreateDirectory(metaDir);
                ToolMetadata.Write(metaDir, new ToolManifest { Source = source, Update = update });
            }

            if (!quiet)
                Console.WriteLine($"Installed {appName} → {Path.Combine(installDir, execName)}");

            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { /* cleanup is best-effort */ }
        }
    }

    // ---- Package install ----

    public static async Task<int> InstallPackageAsync(string packageSpec, string installDir, bool requireSourceLink = false, bool quiet = false)
    {
        // Parse name[@version]
        var parsed = PackageExtractor.ParsePackageReference(packageSpec);
        if (parsed is null)
        {
            Console.Error.WriteLine($"error: invalid package reference '{packageSpec}'");
            return 1;
        }

        string packageName = parsed.Id;
        string? version = string.IsNullOrEmpty(parsed.Version) ? null : parsed.Version;

        if (!quiet)
        {
            Console.WriteLine($"Installing {packageName}{(version is not null ? $" ({version})" : "")} to {installDir}");
            Console.WriteLine("Downloading...");
        }

        using var client = new HttpClient();
        var nuget = new NuGetClient(client);
        var cache = new PackageCache("dotnet-install");

        // Resolve version if not specified
        if (version is null)
        {
            version = await nuget.GetLatestVersionAsync(packageName);
            if (version is null)
            {
                Console.Error.WriteLine($"error: package '{packageName}' not found");
                return 1;
            }
        }

        // Check cache first
        string? cachedPath = cache.TryGet(packageName, version);
        string extractPath;

        if (cachedPath is not null)
        {
            if (!quiet) Console.WriteLine($"Using cached {packageName} {version}");
            extractPath = cachedPath;
        }
        else
        {
            // Download, verify signature, and extract
            try
            {
                string nupkgPath = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Path.GetRandomFileName()}.nupkg");
                await nuget.DownloadToFileAsync(packageName, version, nupkgPath);

                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Path.GetRandomFileName()}");
                    extractPath = PackageExtractor.Extract(nupkgPath, tempDir);

                    // Cache the extracted package
                    string? finalPath = cache.Cache(packageName, version, extractPath);
                    if (finalPath is not null)
                    {
                        try { Directory.Delete(extractPath, true); } catch { }
                        extractPath = finalPath;
                    }
                }
                finally
                {
                    try { File.Delete(nupkgPath); } catch { }
                }
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"error: failed to download {packageName}@{version}: {ex.Message}");
                return 1;
            }
        }

        try
        {
            // Check if this is a pointer package with RID-specific satellite packages
            string? ridPackageId = FindRidSpecificPackage(extractPath);
            if (ridPackageId is not null)
            {
                // Redirect — the RID-specific package prints its own verification
                return await InstallPackageAsync($"{ridPackageId}@{version}", installDir, requireSourceLink);
            }

            // Verify signature from extracted .signature.p7s
            string sigPath = Path.Combine(extractPath, ".signature.p7s");
            if (File.Exists(sigPath))
            {
                var sigResult = PackageSignatureVerifier.VerifySignatureFile(sigPath);
                switch (sigResult.Status)
                {
                    case SignatureStatus.Valid:
                        if (!quiet) PrintSignature(sigResult);
                        break;
                    case SignatureStatus.Unsigned:
                        Console.Error.WriteLine("warning: package is not signed");
                        break;
                    case SignatureStatus.Invalid:
                        Console.Error.WriteLine($"error: package signature verification failed: {sigResult.Reason}");
                        return 1;
                }
            }

            // Find the tool's entry point via DotnetToolSettings.xml
            var toolInfo = FindToolSettings(extractPath);

            if (toolInfo is null)
            {
                Console.Error.WriteLine("error: package does not contain a .NET tool (no DotnetToolSettings.xml)");
                return 1;
            }

            string commandName = toolInfo.CommandName;
            string toolDir = toolInfo.ToolDirectory;

            // CommandName is package-controlled and is used to name the installed binary
            // and its metadata directory. Reject anything that isn't a plain file name so
            // a package cannot escape installDir (e.g. Name="../../victim").
            if (!IsSafeFileName(commandName))
            {
                Console.Error.WriteLine($"error: package declares an invalid command name '{commandName}'.");
                return 1;
            }

            // SourceLink verification (before placement)
            if (requireSourceLink && !SourceLinkCheck.Verify(toolDir))
            {
                Console.Error.WriteLine("error: --require-sourcelink specified but SourceLink verification failed");
                return 1;
            }

            Directory.CreateDirectory(installDir);

            // Only single-file native executables are supported (CLI tools v2).
            // Managed (.dll) tools and multi-file layouts belong to `dotnet tool install`.
            //
            // Locate the payload via the EntryPoint declared in DotnetToolSettings.xml.
            // EntryPoint names the actual file to run, which can differ from the command
            // name the user types; resolving by command name alone falsely rejects such
            // packages.
            string? entryExecPath = ResolveEntryExecutable(toolDir, toolInfo);
            bool isManaged = string.Equals(toolInfo.Runner, "dotnet", StringComparison.OrdinalIgnoreCase)
                || (entryExecPath is not null && entryExecPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            bool isNativeSingleFile = entryExecPath is not null
                && !isManaged
                && IsSingleFile(toolDir);

            if (!isNativeSingleFile)
            {
                Console.Error.WriteLine($"error: '{packageName}' is not a single-file executable tool.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("dotnet-install only installs single-file native tools (CLI tools v2).");
                Console.Error.WriteLine("Install this managed tool with the .NET SDK instead:");
                Console.Error.WriteLine($"  dotnet tool install -g {packageName}");
                return 1;
            }

            // Place under the command name so the tool resolves on PATH as the user
            // expects, even when the packaged executable file name differs.
            string installedExecName = OperatingSystem.IsWindows() ? $"{commandName}.exe" : commandName;
            PlaceSingleFile(entryExecPath!, installDir, installedExecName);

            // Write install metadata for update tracking
            string metaDir = Path.Combine(installDir, $"_{commandName}");
            Directory.CreateDirectory(metaDir);
            ToolMetadata.Write(metaDir, new ToolManifest
            {
                Source = new InstallSource
                {
                    Type = "nuget",
                    Package = packageName,
                    Version = version
                }
            });

            string versionDisplay = version is not null ? $" ({version})" : "";
            if (!quiet) Console.WriteLine($"Installed {commandName}{versionDisplay} → {Path.Combine(installDir, commandName)}");
            return 0;
        }
        finally
        {
            // extractPath is now in the cache, no temp cleanup needed
        }
    }

    internal record ToolSettings(string CommandName, string EntryPoint, string Runner, string ToolDirectory);

    /// <summary>
    /// Resolves the tool's executable file inside <paramref name="toolDir"/> using the
    /// EntryPoint declared in DotnetToolSettings.xml, falling back to the command name.
    /// The command name (what the user types) can differ from the executable file name,
    /// so resolving by command name alone would falsely reject otherwise-valid packages.
    /// Returns the full path to the executable, or null if none is found.
    ///
    /// EntryPoint is package-controlled, so any candidate that is rooted or escapes
    /// <paramref name="toolDir"/> (e.g. "../../etc/passwd") is rejected to prevent a
    /// malicious package from copying an arbitrary host file into the install directory.
    /// </summary>
    internal static string? ResolveEntryExecutable(string toolDir, ToolSettings info)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(info.EntryPoint))
        {
            candidates.Add(info.EntryPoint);
            // Some packages omit the platform extension in EntryPoint.
            if (OperatingSystem.IsWindows()
                && !info.EntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add($"{info.EntryPoint}.exe");
            }
        }

        // Fall back to the command name.
        if (!string.IsNullOrEmpty(info.CommandName))
        {
            candidates.Add(OperatingSystem.IsWindows() ? $"{info.CommandName}.exe" : info.CommandName);
        }

        string toolDirFull = Path.GetFullPath(toolDir);
        foreach (string candidate in candidates)
        {
            // Reject rooted paths outright; they always escape toolDir.
            if (Path.IsPathRooted(candidate))
                continue;

            string path = Path.GetFullPath(Path.Combine(toolDirFull, candidate));
            if (!IsWithinDirectory(toolDirFull, path))
                continue;

            if (File.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Returns true if <paramref name="candidateFull"/> (an already fully-qualified path)
    /// is the directory itself or lies beneath <paramref name="dirFull"/>.
    /// </summary>
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
    /// Returns true if <paramref name="name"/> is a plain file name safe to combine with an
    /// install directory: non-empty, not "."/"..", not rooted, containing no directory
    /// separators or invalid file-name characters. Guards against package-controlled names
    /// escaping the install directory.
    /// </summary>
    internal static bool IsSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name is "." or "..")
            return false;
        if (Path.IsPathRooted(name))
            return false;
        if (name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return true;
    }

    /// <summary>
    /// Resolves the RID-specific package ID from a pointer package's DotnetToolSettings.xml.
    /// Returns null if the package is not a pointer package (i.e., contains tools directly).
    /// </summary>
    internal static string? FindRidSpecificPackage(string extractPath)
    {
        string rid = RuntimeInformation.RuntimeIdentifier;
        var ridFallbacks = GetRidFallbacks(rid);

        var settingsFiles = Directory.GetFiles(extractPath, "DotnetToolSettings.xml", SearchOption.AllDirectories);
        foreach (string f in settingsFiles)
        {
            var doc = System.Xml.Linq.XDocument.Load(f);
            var ridPackages = doc.Descendants("RuntimeIdentifierPackage").ToList();
            if (ridPackages.Count == 0)
                continue;

            // Find the best matching RID-specific package
            foreach (string candidateRid in ridFallbacks)
            {
                var match = ridPackages.FirstOrDefault(rp =>
                    string.Equals(rp.Attribute("RuntimeIdentifier")?.Value, candidateRid, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    string? packageId = match.Attribute("Id")?.Value;
                    if (!string.IsNullOrEmpty(packageId))
                        return packageId;
                }
            }
        }

        return null;
    }

    static ToolSettings? FindToolSettings(string extractPath)
    {
        // NuGet tool packages use the layout: tools/<tfm>/<rid>/DotnetToolSettings.xml
        // We need to select the right RID and prefer the highest compatible TFM.
        string rid = RuntimeInformation.RuntimeIdentifier;
        var ridFallbacks = GetRidFallbacks(rid);

        var candidates = Directory.GetFiles(extractPath, "DotnetToolSettings.xml", SearchOption.AllDirectories)
            .Select(f =>
            {
                var doc = System.Xml.Linq.XDocument.Load(f);
                var command = doc.Descendants("Command").FirstOrDefault();
                if (command is null) return null;

                string dir = Path.GetDirectoryName(f)!;
                string dirRid = Path.GetFileName(dir);
                string? parentDir = Path.GetDirectoryName(dir);
                string dirTfm = parentDir is not null ? Path.GetFileName(parentDir) : "";

                // Score: lower RID index = better match, parse TFM for version ordering
                int ridIndex = ridFallbacks.IndexOf(dirRid);
                if (ridIndex < 0) return null; // incompatible RID

                return new
                {
                    Settings = new ToolSettings(
                        CommandName: command.Attribute("Name")?.Value ?? "",
                        EntryPoint: command.Attribute("EntryPoint")?.Value ?? "",
                        Runner: command.Attribute("Runner")?.Value ?? "",
                        ToolDirectory: dir),
                    RidPriority = ridIndex,
                    Tfm = dirTfm
                };
            })
            .Where(c => c is not null)
            .OrderBy(c => c!.RidPriority)
            .ThenByDescending(c => c!.Tfm, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.FirstOrDefault()?.Settings;
    }

    /// <summary>
    /// Simplified RID fallback chain. Exact match → portable os-arch → os → unix/win → any.
    /// </summary>
    static List<string> GetRidFallbacks(string rid)
    {
        // e.g. rid = "osx-arm64"
        var fallbacks = new List<string> { rid };

        // Split into os and arch parts
        int dash = rid.IndexOf('-');
        if (dash > 0)
        {
            string os = rid[..dash];
            // Add os-only portable RID (e.g. "osx")
            // Don't add if it's the same as the full RID
            if (os != rid)
                fallbacks.Add(os);

            // Add base platform
            string basePlatform = os switch
            {
                "osx" or "maccatalyst" or "ios" or "tvos" => "unix",
                "linux" or "freebsd" or "illumos" or "solaris" or "android" or "browser" or "wasi" => "unix",
                "win" or "win10" => "win",
                _ => ""
            };
            if (!string.IsNullOrEmpty(basePlatform) && basePlatform != os)
                fallbacks.Add(basePlatform);
        }

        fallbacks.Add("any");
        return fallbacks;
    }

    // ---- Project evaluation ----
    // Parses .csproj (XML) or file-based apps (.cs with #:property directives).
    // This avoids assembly loading conflicts and is fully Native AOT compatible.

    record ProjectInfo(string AssemblyName, string OutputType, bool IsNativeAot, bool IsSingleFile, bool IsSelfContained, string? TargetFramework);

    static ProjectInfo EvaluateProject(string projectFile)
    {
        if (IsFileBasedApp(projectFile))
            return EvaluateFileBasedApp(projectFile);

        // Prefer fully-evaluated MSBuild properties so imported props
        // (Directory.Build.props, SDK defaults, RID-conditioned values) are
        // honored. Fall back to raw XML parsing if the SDK query fails.
        return TryEvaluateWithMsBuild(projectFile)
            ?? EvaluateProjectFromXml(projectFile);
    }

    /// <summary>
    /// Evaluates a project's effective properties by asking the SDK
    /// (`dotnet msbuild -getProperty:...`). Returns null if the query fails.
    /// </summary>
    static ProjectInfo? TryEvaluateWithMsBuild(string projectFile)
    {
        try
        {
            string rid = RuntimeInformation.RuntimeIdentifier;
            var psi = new ProcessStartInfo("dotnet")
            {
                ArgumentList =
                {
                    "msbuild", Path.GetFullPath(projectFile),
                    "-getProperty:AssemblyName",
                    "-getProperty:OutputType",
                    "-getProperty:PublishAot",
                    "-getProperty:PublishSingleFile",
                    "-getProperty:SelfContained",
                    "-getProperty:PublishSelfContained",
                    "-getProperty:TargetFramework",
                    "-p:Configuration=Release",
                    $"-p:RuntimeIdentifier={rid}",
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;

            using var doc = JsonDocument.Parse(output);
            if (!doc.RootElement.TryGetProperty("Properties", out var props))
                return null;

            static string Get(JsonElement props, string name) =>
                props.TryGetProperty(name, out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
            static bool IsTrue(JsonElement props, string name) =>
                string.Equals(Get(props, name), "true", StringComparison.OrdinalIgnoreCase);

            string assemblyName = Get(props, "AssemblyName");
            if (string.IsNullOrEmpty(assemblyName))
                assemblyName = Path.GetFileNameWithoutExtension(projectFile);

            string outputType = Get(props, "OutputType");
            if (string.IsNullOrEmpty(outputType))
                return null;

            string tfm = Get(props, "TargetFramework");

            return new ProjectInfo(
                AssemblyName: assemblyName,
                OutputType: outputType,
                IsNativeAot: IsTrue(props, "PublishAot"),
                IsSingleFile: IsTrue(props, "PublishSingleFile"),
                // `SelfContained` is only populated during the Publish target; a
                // project that opts in via `PublishSelfContained` reads false at
                // evaluation time, so honor both.
                IsSelfContained: IsTrue(props, "SelfContained") || IsTrue(props, "PublishSelfContained"),
                TargetFramework: string.IsNullOrEmpty(tfm) ? null : tfm
            );
        }
        catch
        {
            return null;
        }
    }

    static ProjectInfo EvaluateProjectFromXml(string projectFile)
    {
        var doc = XDocument.Load(projectFile);
        var props = doc.Descendants()
            .Where(e => e.Parent?.Name.LocalName == "PropertyGroup");

        string? assemblyName = GetProperty(props, "AssemblyName");
        if (string.IsNullOrEmpty(assemblyName))
            assemblyName = Path.GetFileNameWithoutExtension(projectFile);

        bool isNativeAot = IsPropertyTrue(props, "PublishAot");

        string? sdk = doc.Root?.Attribute("Sdk")?.Value;
        string defaultOutputType = SdkImpliesExecutable(sdk) ? "Exe" : "Library";

        return new ProjectInfo(
            AssemblyName: assemblyName,
            OutputType: GetProperty(props, "OutputType") ?? defaultOutputType,
            IsNativeAot: isNativeAot,
            IsSingleFile: IsPropertyTrue(props, "PublishSingleFile"),
            IsSelfContained: IsPropertyTrue(props, "SelfContained") || IsPropertyTrue(props, "PublishSelfContained"),
            TargetFramework: GetProperty(props, "TargetFramework")
        );
    }

    /// <summary>
    /// Evaluates a file-based app (.cs file with #:property directives).
    /// File-based apps are implicitly executables.
    /// </summary>
    static ProjectInfo EvaluateFileBasedApp(string csFile)
    {
        var properties = ParseFileBasedProperties(csFile);

        string assemblyName = properties.GetValueOrDefault("ToolCommandName")
            ?? properties.GetValueOrDefault("AssemblyName")
            ?? Path.GetFileNameWithoutExtension(csFile);

        return new ProjectInfo(
            AssemblyName: assemblyName,
            OutputType: "Exe",
            IsNativeAot: string.Equals(properties.GetValueOrDefault("PublishAot"), "true", StringComparison.OrdinalIgnoreCase),
            IsSingleFile: string.Equals(properties.GetValueOrDefault("PublishSingleFile"), "true", StringComparison.OrdinalIgnoreCase),
            IsSelfContained: string.Equals(properties.GetValueOrDefault("SelfContained"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(properties.GetValueOrDefault("PublishSelfContained"), "true", StringComparison.OrdinalIgnoreCase),
            TargetFramework: properties.GetValueOrDefault("TargetFramework")
        );
    }

    /// <summary>
    /// Parses #:property directives from a .cs file.
    /// Format: #:property Name=Value
    /// </summary>
    internal static Dictionary<string, string> ParseFileBasedProperties(string csFile)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in File.ReadLines(csFile))
        {
            // Directives must appear before code; stop at first non-directive, non-comment, non-blank line
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("//") || trimmed.StartsWith("#!"))
                continue;
            if (!trimmed.StartsWith("#:"))
                break;

            // #:property Name=Value
            if (trimmed.StartsWith("#:property ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = trimmed["#:property ".Length..];
                int eq = rest.IndexOf('=');
                if (eq > 0)
                {
                    string name = rest[..eq].Trim();
                    string value = rest[(eq + 1)..].Trim();
                    properties[name] = value;
                }
            }
        }

        return properties;
    }

    /// <summary>
    /// Returns true if the file is a .cs file-based app (has #:property directives).
    /// </summary>
    internal static bool IsFileBasedApp(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    static string? GetProperty(IEnumerable<XElement> props, string name) =>
        props.FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    static bool IsPropertyTrue(IEnumerable<XElement> props, string name) =>
        string.Equals(GetProperty(props, name), "true", StringComparison.OrdinalIgnoreCase);

    static bool IsExecutable(string outputType) =>
        outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
        outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SDKs that implicitly set OutputType=Exe (so it won't appear in the raw XML).
    /// </summary>
    internal static bool SdkImpliesExecutable(string? sdk) =>
        sdk is not null && (
            sdk.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
            sdk.Equals("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Warn if the project's TFM is newer than the installed SDK.
    /// e.g. project targets net12.0 but only net11.0 SDK is installed.
    /// </summary>
    static void CheckSdkCompatibility(string tfm)
    {
        // Parse TFM: "net8.0", "net11.0-windows", etc.
        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return;

        string versionPart = tfm[3..];
        int dashIndex = versionPart.IndexOf('-');
        if (dashIndex > 0)
            versionPart = versionPart[..dashIndex];

        if (!Version.TryParse(versionPart, out var tfmVersion))
            return;

        // Get the highest installed runtime major version via `dotnet --list-runtimes`
        var highestRuntime = GetHighestInstalledRuntime();

        if (highestRuntime is not null && tfmVersion.Major > highestRuntime.Major)
        {
            Console.Error.WriteLine($"warning: project targets {tfm} but the highest installed runtime is {highestRuntime.Major}.{highestRuntime.Minor}");
            Console.Error.WriteLine($"The build will likely fail. Install .NET {tfmVersion}: https://dot.net/download");
        }
    }

    /// <summary>
    /// Highest installed Microsoft.NETCore.App runtime version, via `dotnet --list-runtimes`.
    /// Returns null if dotnet is unavailable or no runtimes are reported.
    /// </summary>
    static Version? GetHighestInstalledRuntime()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { "--list-runtimes" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;

            Version? highest = null;
            foreach (string line in output.Split('\n'))
            {
                // Format: "Microsoft.NETCore.App 11.0.0-preview.2.26159.112 [/path/...]"
                if (!line.StartsWith("Microsoft.NETCore.App ", StringComparison.Ordinal))
                    continue;

                string rest = line["Microsoft.NETCore.App ".Length..].TrimStart();
                int space = rest.IndexOf(' ');
                string ver = space > 0 ? rest[..space] : rest;

                // Strip pre-release suffix (Version.TryParse doesn't handle SemVer tags)
                int dash = ver.IndexOf('-');
                if (dash > 0) ver = ver[..dash];

                if (Version.TryParse(ver, out var v) && (highest is null || v > highest))
                    highest = v;
            }

            return highest;
        }
        catch
        {
            return null;
        }
    }

    // ---- Publish (out-of-process) ----

    static bool Publish(string projectFile, string outputDir)
    {
        // Preflight: check that the .NET SDK is available
        try
        {
            var check = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var probe = Process.Start(check);
            probe?.WaitForExit();
            if (probe is null || probe.ExitCode != 0)
            {
                Console.Error.WriteLine("error: .NET SDK not found. Building from source requires the SDK.");
                Console.Error.WriteLine();
                Console.Error.WriteLine("To install the SDK: https://dot.net/download");
                Console.Error.WriteLine("To install a pre-built tool instead: dotnet-install --package <name>");
                return false;
            }
        }
        catch
        {
            Console.Error.WriteLine("error: .NET SDK not found. Building from source requires the SDK.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("To install the SDK: https://dot.net/download");
            Console.Error.WriteLine("To install a pre-built tool instead: dotnet-install --package <name>");
            return false;
        }

        string fullProjectPath = Path.GetFullPath(projectFile);
        string projectDir = Path.GetDirectoryName(fullProjectPath)!;
        string rid = RuntimeInformation.RuntimeIdentifier;

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Publish with the project's own configuration. The candidate check
        // already verified (via property evaluation) that the project opts into
        // Native AOT or self-contained single-file, so nothing is injected here
        // to coerce single-file/self-contained publishing — the project decides.
        psi.ArgumentList.Add("publish");
        psi.ArgumentList.Add(fullProjectPath);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add(rid);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputDir);

        using var process = Process.Start(psi)!;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string output = stdout.Result;
            string errors = stderr.Result;
            if (!string.IsNullOrWhiteSpace(errors))
                Console.Error.Write(errors);
            if (!string.IsNullOrWhiteSpace(output))
                Console.Error.Write(output);
        }

        return process.ExitCode == 0;
    }

    // ---- Detection ----

    internal static bool IsSingleFile(string publishDir)
    {
        // Single-file = only one significant file. Recurse so a nested payload
        // (e.g. lib/dependency.so) is not mistaken for a self-contained
        // single-file publish, but ignore debug symbols (.pdb/.dbg, macOS
        // .dSYM bundles) and tool metadata.
        var files = Directory.GetFiles(publishDir, "*", SearchOption.AllDirectories)
            .Where(f => !IsIgnoredPublishArtifact(publishDir, f))
            .ToList();

        return files.Count == 1;
    }

    static bool IsIgnoredPublishArtifact(string publishDir, string file)
    {
        string name = Path.GetFileName(file);
        if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".r2rmap", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DotnetToolSettings.xml", StringComparison.OrdinalIgnoreCase))
            return true;

        // Ignore anything inside a macOS .dSYM debug-symbols bundle.
        string rel = Path.GetRelativePath(publishDir, file);
        return rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(seg => seg.EndsWith(".dSYM", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Placement ----

    static void PlaceSingleFile(string executablePath, string installDir, string executableName)
    {
        string dest = Path.Combine(installDir, executableName);

        // Remove existing file/symlink first — File.Copy follows symlinks,
        // which would write into a stale _appname/ directory from a prior
        // multi-file install.
        if (File.Exists(dest) || IsSymlink(dest))
            File.Delete(dest);

        File.Copy(executablePath, dest);
        SetExecutable(dest);
    }

    // ---- Helpers ----

    static void SetExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    static bool IsSymlink(string path)
    {
        try { return new FileInfo(path).LinkTarget is not null; }
        catch { return false; }
    }

    // ---- Signature display ----

    static void PrintSignature(SignatureVerificationResult sigResult)
    {
        if (sigResult.SignatureType == SignatureType.Author && sigResult.CounterSignature is { IsValid: true })
        {
            Console.WriteLine($"Verified: {sigResult.CounterSignature.Publisher ?? "signed"} ({sigResult.CounterSignature.SignatureType})");
            Console.WriteLine($"Author: {sigResult.Publisher}");
        }
        else
        {
            Console.WriteLine($"Verified: {sigResult.Publisher ?? "signed"} ({sigResult.SignatureType})");
        }
    }
}
