using NuGetFetch;

/// <summary>
/// Run a NuGet tool package without installing it permanently.
/// Downloads to a temp directory, executes, then cleans up.
/// Like npx for .NET tools.
/// </summary>
static class RunCommand
{
    public static async Task<int> RunAsync(string packageSpec, string[] toolArgs, bool allowRollForward = false)
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

        using var client = new HttpClient();
        var nuget = new NuGetClient(client);
        var cache = new PackageCache("dotnet-install");

        // Resolve version
        if (version is null)
        {
            version = await nuget.GetLatestVersionAsync(packageName);
            if (version is null)
            {
                Console.Error.WriteLine($"error: package '{packageName}' not found");
                return 1;
            }
        }

        // Use cache if available, otherwise download
        string? cachedPath = cache.TryGet(packageName, version);
        string extractPath;
        bool ownedExtract = false;

        if (cachedPath is not null)
        {
            extractPath = cachedPath;
        }
        else
        {
            try
            {
                string nupkgPath = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Path.GetRandomFileName()}.nupkg");
                await nuget.DownloadToFileAsync(packageName, version, nupkgPath);

                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Path.GetRandomFileName()}");
                    extractPath = PackageExtractor.Extract(nupkgPath, tempDir);
                    ownedExtract = true;

                    string? finalPath = cache.Cache(packageName, version, extractPath);
                    if (finalPath is not null)
                    {
                        try { Directory.Delete(extractPath, true); } catch { }
                        extractPath = finalPath;
                        ownedExtract = false;
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
            string? ridPackageId = Installer.FindRidSpecificPackage(extractPath);
            if (ridPackageId is not null)
            {
                return await RunAsync($"{ridPackageId}@{version}", toolArgs, allowRollForward);
            }

            return RunFromExtracted(extractPath, packageName, toolArgs, allowRollForward);
        }
        finally
        {
            if (ownedExtract)
            {
                try { Directory.Delete(extractPath, true); } catch { }
            }
        }
    }

    static int RunFromExtracted(string extractPath, string packageName, string[] toolArgs, bool allowRollForward)
    {
        // Find the tool entry point
        var toolInfo = FindToolSettings(extractPath);
        if (toolInfo is null)
        {
            Console.Error.WriteLine("error: package does not contain a .NET tool");
            return 1;
        }

        string commandName = toolInfo.CommandName;
        string toolDir = toolInfo.ToolDirectory;

        // Check for native executable
        string nativeExecName = OperatingSystem.IsWindows() ? $"{commandName}.exe" : commandName;
        string nativeExecPath = Path.Combine(toolDir, nativeExecName);
        bool isNative = File.Exists(nativeExecPath) && !File.Exists(Path.Combine(toolDir, $"{commandName}.dll"));

        if (isNative)
        {
            // Native tool — exec directly
            SetExecutable(nativeExecPath);
            return ExecProcess(nativeExecPath, toolArgs);
        }

        // Managed tool — find DLL entry point and run via dotnet exec
        string entryDll = Path.Combine(toolDir, toolInfo.EntryPoint);
        if (!File.Exists(entryDll))
        {
            Console.Error.WriteLine($"error: entry point not found: {toolInfo.EntryPoint}");
            return 1;
        }

        string dotnetPath = FindDotnet();
        var execArgs = new List<string>();
        execArgs.Add("exec");

        if (allowRollForward)
        {
            execArgs.Add("--roll-forward");
            execArgs.Add("LatestMajor");
        }

        execArgs.Add(entryDll);
        execArgs.AddRange(toolArgs);

        return ExecProcess(dotnetPath, execArgs.ToArray());
    }

    static int ExecProcess(string program, string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(program)
        {
            UseShellExecute = false
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine($"error: failed to start {program}");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    static string FindDotnet()
    {
        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (dotnetRoot is not null)
        {
            string candidate = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }
        return "dotnet";
    }

    static void SetExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
    }

    record ToolSettings(string CommandName, string EntryPoint, string ToolDirectory);

    static ToolSettings? FindToolSettings(string extractPath)
    {
        var settingsFiles = Directory.GetFiles(extractPath, "DotnetToolSettings.xml", SearchOption.AllDirectories);
        if (settingsFiles.Length == 0)
            return null;

        foreach (string settingsFile in settingsFiles)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(settingsFile);
                var commands = doc.Descendants()
                    .Where(e => e.Name.LocalName == "Command")
                    .ToList();

                if (commands.Count == 0) continue;

                var command = commands[0];
                string? commandName = command.Attribute("Name")?.Value;
                string? entryPoint = command.Attribute("EntryPoint")?.Value;
                string? runner = command.Attribute("Runner")?.Value;

                if (commandName is null || entryPoint is null)
                    continue;

                string toolDir = Path.GetDirectoryName(settingsFile)!;
                return new ToolSettings(commandName, entryPoint, toolDir);
            }
            catch { }
        }

        return null;
    }
}
