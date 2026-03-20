using System.Diagnostics;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using NuGetFetch;

static class Installer
{
    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "bin");

    public static string LocalBinDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    public static int Install(string projectFile, string installDir, InstallSource? source = null, bool requireSourceLink = false)
    {
        // 1. Evaluate the project to read properties before building
        var info = EvaluateProject(projectFile);

        if (!IsExecutable(info.OutputType))
        {
            Console.Error.WriteLine($"error: not an executable project (OutputType={info.OutputType})");
            return 1;
        }

        string appName = info.AssemblyName;
        string mode = info.IsNativeAot ? "Native AOT" :
                      info.IsSingleFile ? "single-file" : "framework-dependent";

        // Pre-flight: warn if the project's TFM may not be buildable
        if (info.TargetFramework is not null)
            CheckSdkCompatibility(info.TargetFramework);

        Console.WriteLine($"Installing {appName} to {installDir}");
        Console.WriteLine($"Publishing ({mode}, Release)...");

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

            // 5. Detect single-file vs multi-file and place
            bool singleFile = IsSingleFile(tempDir);
            Directory.CreateDirectory(installDir);

            if (singleFile)
            {
                PlaceSingleFile(execPath, installDir, execName);
            }
            else
            {
                PlaceMultiFile(tempDir, installDir, appName, execName);
            }

            // Write install metadata (for update tracking)
            if (source is not null)
            {
                string metaDir = Path.Combine(installDir, $"_{appName}");
                Directory.CreateDirectory(metaDir);
                ToolMetadata.Write(metaDir, new ToolManifest { Source = source });
            }

            string display = singleFile ? execName :
                             OperatingSystem.IsWindows() ? $"{appName}.cmd" : execName;
            Console.WriteLine($"Installed {appName} → {Path.Combine(installDir, display)}");

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

    public static async Task<int> InstallPackageAsync(string packageSpec, string installDir, bool allowRollForward = false, bool requireSourceLink = false)
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

        Console.WriteLine($"Installing {packageName}{(version is not null ? $" ({version})" : "")} to {installDir}");
        Console.WriteLine("Downloading...");

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
            Console.WriteLine($"Using cached {packageName} {version}");
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

        // Verify signature from extracted .signature.p7s (works for both fresh and cached)
        string sigPath = Path.Combine(extractPath, ".signature.p7s");
        if (File.Exists(sigPath))
        {
            var sigResult = PackageSignatureVerifier.VerifySignatureFile(sigPath);
            switch (sigResult.Status)
            {
                case SignatureStatus.Valid:
                    PrintSignature(sigResult);
                    break;
                case SignatureStatus.Unsigned:
                    Console.Error.WriteLine("warning: package is not signed");
                    break;
                case SignatureStatus.Invalid:
                    Console.Error.WriteLine($"error: package signature verification failed: {sigResult.Reason}");
                    return 1;
            }
        }

        try
        {
            // Check if this is a pointer package with RID-specific satellite packages
            string? ridPackageId = FindRidSpecificPackage(extractPath);
            if (ridPackageId is not null)
            {
                // Redirect to the RID-specific package
                return await InstallPackageAsync($"{ridPackageId}@{version}", installDir, allowRollForward, requireSourceLink);
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

            // SourceLink verification (before placement)
            if (requireSourceLink && !SourceLinkCheck.Verify(toolDir))
            {
                Console.Error.WriteLine("error: --require-sourcelink specified but SourceLink verification failed");
                return 1;
            }

            Directory.CreateDirectory(installDir);

            // Check if the tool directory contains a native executable
            string nativeExecName = OperatingSystem.IsWindows() ? $"{commandName}.exe" : commandName;
            string nativeExecPath = Path.Combine(toolDir, nativeExecName);
            bool isNative = File.Exists(nativeExecPath) && !File.Exists(Path.Combine(toolDir, $"{commandName}.dll"));

            if (isNative && IsSingleFile(toolDir))
            {
                // Native single-file: place directly
                PlaceSingleFile(nativeExecPath, installDir, nativeExecName);

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
            }
            else
            {
                // Managed or multi-file: copy tool directory, create launcher
                string appDir = Path.Combine(installDir, $"_{commandName}");
                if (Directory.Exists(appDir))
                    Directory.Delete(appDir, true);

                CopyDirectory(toolDir, appDir);

                if (isNative)
                {
                    // Native multi-file: symlink/shim to the executable
                    CreateLink(installDir, appDir, commandName, nativeExecName);
                }
                else
                {
                    // Managed tool: check runtime compat, write metadata, create host symlink
                    string entryDll = toolInfo.EntryPoint;
                    string? runtimeConfigPath = FindRuntimeConfig(appDir, commandName);

                    if (runtimeConfigPath is not null)
                    {
                        var compat = RuntimeCompat.CheckCompatibility(runtimeConfigPath);
                        if (!compat.CanRun)
                        {
                            if (compat.RollForwardWouldHelp && !allowRollForward)
                            {
                                Console.Error.WriteLine($"error: {commandName} requires {compat.RequiredFramework} {compat.RequiredVersion} which is not installed.");
                                Console.Error.WriteLine();
                                Console.Error.WriteLine("This can be resolved by:");
                                Console.Error.WriteLine($"  dotnet install --package {packageSpec} --allow-roll-forward");
                                Console.Error.WriteLine($"  Install .NET {compat.RequiredVersion}: https://dot.net/download");
                                return 1;
                            }

                            if (!compat.RollForwardWouldHelp)
                            {
                                Console.Error.WriteLine($"error: {commandName} requires {compat.RequiredFramework} {compat.RequiredVersion} which is not installed.");
                                Console.Error.WriteLine();
                                Console.Error.WriteLine("To resolve this:");
                                Console.Error.WriteLine($"  Install .NET {compat.RequiredVersion}: https://dot.net/download");
                                return 1;
                            }

                            // allowRollForward is true and roll-forward would help — proceed
                        }
                    }

                    // Write tool metadata sidecar
                    ToolMetadata.Write(appDir, new ToolManifest
                    {
                        EntryPoint = entryDll,
                        RollForward = allowRollForward,
                        Source = new InstallSource
                        {
                            Type = "nuget",
                            Package = packageName,
                            Version = version
                        }
                    });

                    CreateManagedLauncher(installDir, appDir, commandName, entryDll, allowRollForward);
                }
            }

            string versionDisplay = version is not null ? $" ({version})" : "";
            Console.WriteLine($"Installed {commandName}{versionDisplay} → {Path.Combine(installDir, commandName)}");
            return 0;
        }
        finally
        {
            // extractPath is now in the cache, no temp cleanup needed
        }
    }

    record ToolSettings(string CommandName, string EntryPoint, string Runner, string ToolDirectory);

    /// <summary>
    /// Resolves the RID-specific package ID from a pointer package's DotnetToolSettings.xml.
    /// Returns null if the package is not a pointer package (i.e., contains tools directly).
    /// </summary>
    static string? FindRidSpecificPackage(string extractPath)
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

    static void CreateManagedLauncher(string installDir, string appDir, string commandName, string entryDll, bool rollForward)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: .cmd shim that delegates to dotnet-install in host mode
            string shimPath = Path.Combine(installDir, $"{commandName}.cmd");
            string dotnetInstallPath = Environment.ProcessPath ?? "dotnet-install";
            File.WriteAllText(shimPath, $"""@"{dotnetInstallPath}" --host {commandName} %*{"\r\n"}""");
        }
        else
        {
            // Unix: symlink to dotnet-install binary (BusyBox model)
            string linkPath = Path.Combine(installDir, commandName);
            if (File.Exists(linkPath) || IsSymlink(linkPath))
                File.Delete(linkPath);

            string hostPath = Environment.ProcessPath
                ?? Path.Combine(installDir, "dotnet-install");

            // Create relative symlink if both are in the same directory
            string hostDir = Path.GetDirectoryName(hostPath)!;
            string target = string.Equals(Path.GetFullPath(hostDir), Path.GetFullPath(installDir), StringComparison.Ordinal)
                ? Path.GetFileName(hostPath)
                : hostPath;

            File.CreateSymbolicLink(linkPath, target);
        }
    }

    /// <summary>
    /// Find the runtimeconfig.json for a tool in its app directory.
    /// </summary>
    static string? FindRuntimeConfig(string appDir, string commandName)
    {
        // Try exact name first, then search
        string exact = Path.Combine(appDir, $"{commandName}.runtimeconfig.json");
        if (File.Exists(exact)) return exact;

        var configs = Directory.GetFiles(appDir, "*.runtimeconfig.json");
        return configs.Length > 0 ? configs[0] : null;
    }

    static void CreateLink(string installDir, string appDir, string appName, string execName)
    {
        if (OperatingSystem.IsWindows())
        {
            string shimPath = Path.Combine(installDir, $"{appName}.cmd");
            File.WriteAllText(shimPath, $"""@"%~dp0\_{appName}\{execName}" %*{"\r\n"}""");
        }
        else
        {
            string linkPath = Path.Combine(installDir, execName);
            if (File.Exists(linkPath) || IsSymlink(linkPath))
                File.Delete(linkPath);

            File.CreateSymbolicLink(linkPath, Path.Combine($"_{appName}", execName));
        }

        SetExecutable(Path.Combine(appDir, execName));
    }

    // ---- Project evaluation (direct XML parsing) ----
    // Parses the .csproj directly instead of using the MSBuild API.
    // This avoids assembly loading conflicts and is fully Native AOT compatible.

    record ProjectInfo(string AssemblyName, string OutputType, bool IsNativeAot, bool IsSingleFile, string? TargetFramework);

    static ProjectInfo EvaluateProject(string projectFile)
    {
        var doc = XDocument.Load(projectFile);
        var props = doc.Descendants()
            .Where(e => e.Parent?.Name.LocalName == "PropertyGroup");

        string? assemblyName = GetProperty(props, "AssemblyName");
        if (string.IsNullOrEmpty(assemblyName))
            assemblyName = Path.GetFileNameWithoutExtension(projectFile);

        return new ProjectInfo(
            AssemblyName: assemblyName,
            OutputType: GetProperty(props, "OutputType") ?? "Library",
            IsNativeAot: IsPropertyTrue(props, "PublishAot"),
            IsSingleFile: IsPropertyTrue(props, "PublishSingleFile"),
            TargetFramework: GetProperty(props, "TargetFramework")
        );
    }

    static string? GetProperty(IEnumerable<XElement> props, string name) =>
        props.FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    static bool IsPropertyTrue(IEnumerable<XElement> props, string name) =>
        string.Equals(GetProperty(props, name), "true", StringComparison.OrdinalIgnoreCase);

    static bool IsExecutable(string outputType) =>
        outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
        outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);

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

        // Get installed SDK version
        var runtimes = RuntimeCompat.GetInstalledRuntimes();
        var highestRuntime = runtimes
            .Where(r => r.Name == "Microsoft.NETCore.App")
            .Select(r =>
            {
                // Strip pre-release suffix (e.g. "11.0.0-preview.2.26159.112" → "11.0.0")
                // Version.TryParse doesn't handle SemVer pre-release tags
                string ver = r.Version;
                int dash = ver.IndexOf('-');
                if (dash > 0) ver = ver[..dash];
                return Version.TryParse(ver, out var v) ? v : null;
            })
            .Where(v => v is not null)
            .Max();

        if (highestRuntime is not null && tfmVersion.Major > highestRuntime.Major)
        {
            Console.Error.WriteLine($"warning: project targets {tfm} but the highest installed runtime is {highestRuntime.Major}.{highestRuntime.Minor}");
            Console.Error.WriteLine($"The build will likely fail. Install .NET {tfmVersion}: https://dot.net/download");
        }
    }

    // ---- Publish (out-of-process) ----

    static bool Publish(string projectFile, string outputDir)
    {
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

        psi.ArgumentList.Add("publish");
        psi.ArgumentList.Add(fullProjectPath);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add(rid);
        psi.ArgumentList.Add("--self-contained");
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

    static bool IsSingleFile(string publishDir)
    {
        // Single-file = only one significant file (ignoring debug symbols and tool metadata)
        var files = Directory.GetFiles(publishDir)
            .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                      && !f.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase)
                      && !Path.GetFileName(f).Equals("DotnetToolSettings.xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return files.Count == 1;
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

    static void PlaceMultiFile(string publishDir, string installDir, string appName, string executableName)
    {
        string appDir = Path.Combine(installDir, $"_{appName}");

        // Remove previous installation
        if (Directory.Exists(appDir))
            Directory.Delete(appDir, true);

        // Move published output into _appname/ (faster than copy, avoids permission issues)
        Directory.Move(publishDir, appDir);

        if (OperatingSystem.IsWindows())
        {
            // Create a CMD shim: app.cmd → _app/app.exe
            string shimPath = Path.Combine(installDir, $"{appName}.cmd");
            File.WriteAllText(shimPath, $"""@"%~dp0\_{appName}\{executableName}" %*{"\r\n"}""");
        }
        else
        {
            // Create a relative symlink: app → _app/app
            string linkPath = Path.Combine(installDir, executableName);

            if (File.Exists(linkPath) || IsSymlink(linkPath))
                File.Delete(linkPath);

            string target = Path.Combine($"_{appName}", executableName);
            File.CreateSymbolicLink(linkPath, target);
        }

        SetExecutable(Path.Combine(appDir, executableName));
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

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
