using System.Diagnostics;
using System.Xml.Linq;
using DotnetInspector.Packages;
using System.Runtime.InteropServices;

static class Installer
{
    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "bin");

    public static string LocalBinDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    public static int Install(string projectFile, string installDir)
    {
        // 1. Evaluate the project to read properties before building
        var info = EvaluateProject(projectFile);

        if (!IsExecutable(info.OutputType))
        {
            Console.Error.WriteLine($"  error: not an executable project (OutputType={info.OutputType})");
            return 1;
        }

        string appName = info.AssemblyName;
        string mode = info.IsNativeAot ? "Native AOT" :
                      info.IsSingleFile ? "single-file" : "framework-dependent";

        Console.WriteLine($"  Installing {appName} to {installDir}");
        Console.WriteLine($"  Publishing ({mode}, Release)...");

        // 2. Execute the Publish target via MSBuild API
        string tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Path.GetRandomFileName()}");

        try
        {
            if (!Publish(projectFile, tempDir))
            {
                Console.Error.WriteLine("  error: publish failed");
                return 1;
            }

            // 3. Locate executable in publish output
            string execName = OperatingSystem.IsWindows() ? $"{appName}.exe" : appName;
            string execPath = Path.Combine(tempDir, execName);

            if (!File.Exists(execPath))
            {
                Console.Error.WriteLine($"  error: '{execName}' not found in publish output");
                return 1;
            }

            // 4. Detect single-file vs multi-file and place
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

            string display = singleFile ? execName :
                             OperatingSystem.IsWindows() ? $"{appName}.cmd" : execName;
            Console.WriteLine($"  Installed {appName} → {Path.Combine(installDir, display)}");

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

    public static async Task<int> InstallPackageAsync(string packageSpec, string installDir)
    {
        // Parse name[@version]
        var (packageName, version) = PackageExtractor.ParsePackageReference(packageSpec);

        Console.WriteLine($"  Installing {packageName}{(version is not null ? $" ({version})" : "")} to {installDir}");
        Console.WriteLine("  Downloading...");

        // Download and extract the .nupkg directly via NuGet V3 API
        NuGetCache.Initialize("dotnet-install");
        using var client = new HttpClient();

        var outcome = await PackageExtractor.ExtractPackageAsync(
            client, packageSpec, log: msg => Console.WriteLine($"  {msg}"));

        if (!outcome.IsSuccess)
        {
            Console.Error.WriteLine($"  error: {outcome.ErrorMessage}");
            return 1;
        }

        var result = outcome.Result!;
        string extractPath = result.ExtractPath;

        try
        {
            // Find the tool's entry point via DotnetToolSettings.xml
            var toolInfo = FindToolSettings(extractPath);

            if (toolInfo is null)
            {
                Console.Error.WriteLine("  error: package does not contain a .NET tool (no DotnetToolSettings.xml)");
                return 1;
            }

            string commandName = toolInfo.CommandName;
            string toolDir = toolInfo.ToolDirectory;

            Directory.CreateDirectory(installDir);

            // Check if the tool directory contains a native executable
            string nativeExecName = OperatingSystem.IsWindows() ? $"{commandName}.exe" : commandName;
            string nativeExecPath = Path.Combine(toolDir, nativeExecName);
            bool isNative = File.Exists(nativeExecPath) && !File.Exists(Path.Combine(toolDir, $"{commandName}.dll"));

            if (isNative && IsSingleFile(toolDir))
            {
                // Native single-file: place directly
                PlaceSingleFile(nativeExecPath, installDir, nativeExecName);
            }
            else
            {
                // Managed or multi-file: copy tool directory, create launcher
                string appDir = Path.Combine(installDir, $"_{commandName}");
                if (Directory.Exists(appDir))
                    Directory.Delete(appDir, true);

                Directory.Move(toolDir, appDir);

                if (isNative)
                {
                    // Native multi-file: symlink/shim to the executable
                    CreateLink(installDir, appDir, commandName, nativeExecName);
                }
                else
                {
                    // Managed: create a launcher script that calls dotnet exec
                    string entryDll = toolInfo.EntryPoint;
                    CreateManagedLauncher(installDir, appDir, commandName, entryDll);
                }
            }

            string versionDisplay = result.Version is not null ? $" ({result.Version})" : "";
            Console.WriteLine($"  Installed {commandName}{versionDisplay} → {Path.Combine(installDir, commandName)}");
            return 0;
        }
        finally
        {
            // Clean up temp dir if not cached
            if (result.TempDir is not null)
            {
                try { Directory.Delete(result.TempDir, true); }
                catch { /* best-effort */ }
            }
        }
    }

    record ToolSettings(string CommandName, string EntryPoint, string Runner, string ToolDirectory);

    static ToolSettings? FindToolSettings(string extractPath)
    {
        // Search for DotnetToolSettings.xml in the tools/ directory hierarchy
        foreach (var settingsFile in Directory.GetFiles(extractPath, "DotnetToolSettings.xml", SearchOption.AllDirectories))
        {
            var doc = System.Xml.Linq.XDocument.Load(settingsFile);
            var command = doc.Descendants("Command").FirstOrDefault();

            if (command is not null)
            {
                return new ToolSettings(
                    CommandName: command.Attribute("Name")?.Value ?? "",
                    EntryPoint: command.Attribute("EntryPoint")?.Value ?? "",
                    Runner: command.Attribute("Runner")?.Value ?? "",
                    ToolDirectory: Path.GetDirectoryName(settingsFile)!);
            }
        }

        return null;
    }

    static void CreateManagedLauncher(string installDir, string appDir, string commandName, string entryDll)
    {
        string appDirName = $"_{commandName}";

        if (OperatingSystem.IsWindows())
        {
            string shimPath = Path.Combine(installDir, $"{commandName}.cmd");
            File.WriteAllText(shimPath, $"""@dotnet exec "%~dp0\{appDirName}\{entryDll}" %*{"\r\n"}""");
        }
        else
        {
            // Place launcher script directly in bin dir (not a symlink — it needs
            // to resolve its own directory to find the DLL)
            string launcherPath = Path.Combine(installDir, commandName);
            if (File.Exists(launcherPath) || IsSymlink(launcherPath))
                File.Delete(launcherPath);

            File.WriteAllText(launcherPath,
                $"#!/bin/sh\nDIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\nexec dotnet exec \"$DIR/{appDirName}/{entryDll}\" \"$@\"\n");
            SetExecutable(launcherPath);
        }
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

    record ProjectInfo(string AssemblyName, string OutputType, bool IsNativeAot, bool IsSingleFile);

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
            IsSingleFile: IsPropertyTrue(props, "PublishSingleFile")
        );
    }

    static string? GetProperty(IEnumerable<XElement> props, string name) =>
        props.FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    static bool IsPropertyTrue(IEnumerable<XElement> props, string name) =>
        string.Equals(GetProperty(props, name), "true", StringComparison.OrdinalIgnoreCase);

    static bool IsExecutable(string outputType) =>
        outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
        outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);

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
        // Single-file = only one significant file (ignoring .pdb debug symbols)
        var files = Directory.GetFiles(publishDir)
            .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return files.Count == 1;
    }

    // ---- Placement ----

    static void PlaceSingleFile(string executablePath, string installDir, string executableName)
    {
        string dest = Path.Combine(installDir, executableName);
        File.Copy(executablePath, dest, overwrite: true);
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

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
