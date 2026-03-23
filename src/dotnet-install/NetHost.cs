using System.Runtime.InteropServices;

/// <summary>
/// Result of hostfxr discovery.
/// </summary>
/// <param name="HostFxrPath">Full path to the hostfxr native library, or null if not found.</param>
/// <param name="DotnetRoot">The .NET root directory used for discovery.</param>
/// <param name="Source">How the dotnet root was discovered (e.g., "DOTNET_ROOT", "PATH", "registered").</param>
sealed record DiscoveryResult(string? HostFxrPath, string? DotnetRoot, string Source);

/// <summary>
/// Managed implementation of the native nethost component's <c>get_hostfxr_path</c> discovery algorithm.
/// Searches for the hostfxr library using environment variables, registered install locations,
/// PATH, and well-known default directories.
/// </summary>
static class NetHost
{
    /// <summary>
    /// Discover the hostfxr library path using the same search strategy as the native nethost component.
    /// </summary>
    /// <param name="dotnetRoot">Optional explicit dotnet root. If provided, only this location is searched.</param>
    internal static DiscoveryResult Discover(string? dotnetRoot = null)
    {
        // 1. Explicit root override
        if (dotnetRoot is not null)
        {
            string? path = FindHostFxrInRoot(dotnetRoot);
            return new(path, dotnetRoot, "explicit");
        }

        // 2. Architecture-specific env var: DOTNET_ROOT_ARM64, DOTNET_ROOT_X64, etc.
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "X64",
            Architecture.X86 => "X86",
            Architecture.Arm64 => "ARM64",
            Architecture.Arm => "ARM",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant(),
        };

        string archEnvVar = $"DOTNET_ROOT_{arch}";
        string? archRoot = Environment.GetEnvironmentVariable(archEnvVar);
        if (!string.IsNullOrEmpty(archRoot))
        {
            string? path = FindHostFxrInRoot(archRoot);
            if (path is not null)
                return new(path, archRoot, archEnvVar);
        }

        // 3. Generic DOTNET_ROOT env var
        string? genericRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(genericRoot))
        {
            string? path = FindHostFxrInRoot(genericRoot);
            if (path is not null)
                return new(path, genericRoot, "DOTNET_ROOT");
        }

        // 4. Registered install locations (platform-specific)
        foreach (var (registeredRoot, source) in GetRegisteredInstallLocations(arch))
        {
            string? path = FindHostFxrInRoot(registeredRoot);
            if (path is not null)
                return new(path, registeredRoot, source);
        }

        // 5. PATH-based discovery (find dotnet executable, resolve symlinks)
        string? pathRoot = FindDotnetRootOnPath();
        if (pathRoot is not null)
        {
            string? path = FindHostFxrInRoot(pathRoot);
            if (path is not null)
                return new(path, pathRoot, "PATH");
        }

        // 6. Default install locations
        string defaultRoot = GetDefaultInstallLocation();
        {
            string? path = FindHostFxrInRoot(defaultRoot);
            if (path is not null)
                return new(path, defaultRoot, "default");
        }

        // Nothing found — return the best dotnet root we could determine
        string? bestRoot = archRoot ?? genericRoot ?? pathRoot ?? defaultRoot;
        return new(null, bestRoot, "not_found");
    }

    /// <summary>
    /// Find the highest-versioned hostfxr library within a dotnet root directory.
    /// Searches <c>&lt;root&gt;/host/fxr/&lt;version&gt;/hostfxr[.dll|.dylib|.so]</c>.
    /// </summary>
    internal static string? FindHostFxrInRoot(string dotnetRoot)
    {
        string fxrDir = Path.Combine(dotnetRoot, "host", "fxr");
        if (!Directory.Exists(fxrDir))
            return null;

        // Find the highest versioned hostfxr directory
        string? best = null;
        Version bestVersion = new();

        foreach (string dir in Directory.GetDirectories(fxrDir))
        {
            string name = Path.GetFileName(dir);
            if (Version.TryParse(name, out var v) && v > bestVersion)
            {
                bestVersion = v;
                best = dir;
            }
        }

        if (best is null)
            return null;

        string libName = OperatingSystem.IsWindows() ? "hostfxr.dll" :
                         OperatingSystem.IsMacOS() ? "libhostfxr.dylib" :
                         "libhostfxr.so";

        string libPath = Path.Combine(best, libName);
        return File.Exists(libPath) ? libPath : null;
    }

    /// <summary>
    /// Get registered install locations from platform-specific sources.
    /// On Windows: registry. On Unix: /etc/dotnet/install_location files.
    /// </summary>
    static IEnumerable<(string Root, string Source)> GetRegisteredInstallLocations(string arch)
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var result in GetWindowsRegisteredLocations(arch))
                yield return result;
        }
        else
        {
            foreach (var result in GetUnixRegisteredLocations(arch))
                yield return result;
        }
    }

    /// <summary>
    /// Windows: read install location from registry.
    /// HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\{arch}\InstallLocation
    /// </summary>
    static IEnumerable<(string Root, string Source)> GetWindowsRegisteredLocations(string arch)
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        // Registry access requires Microsoft.Win32.Registry which isn't available
        // cross-platform in NativeAOT. Fall through to PATH/default discovery.
        // The env var DOTNET_ROOT is the primary mechanism on Windows.
        yield break;
    }

    /// <summary>
    /// Unix: read install location from /etc/dotnet/ files.
    /// Checks architecture-specific file first, then the generic file.
    /// </summary>
    static IEnumerable<(string Root, string Source)> GetUnixRegisteredLocations(string arch)
    {
        // Architecture-specific: /etc/dotnet/install_location_<arch>
        string archFile = $"/etc/dotnet/install_location_{arch.ToLowerInvariant()}";
        string? archLocation = ReadInstallLocationFile(archFile);
        if (archLocation is not null)
            yield return (archLocation, $"install_location_{arch.ToLowerInvariant()}");

        // Generic: /etc/dotnet/install_location
        string genericFile = "/etc/dotnet/install_location";
        string? genericLocation = ReadInstallLocationFile(genericFile);
        if (genericLocation is not null)
            yield return (genericLocation, "install_location");
    }

    /// <summary>
    /// Read the first line of an install_location file, trimmed.
    /// </summary>
    static string? ReadInstallLocationFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            string? line = File.ReadLines(path).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find the dotnet root by locating the dotnet executable on PATH and resolving symlinks.
    /// </summary>
    static string? FindDotnetRootOnPath()
    {
        string dotnetExe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is null)
            return null;

        char sep = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (string dir in pathVar.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, dotnetExe);
            if (!File.Exists(candidate))
                continue;

            // Resolve symlinks to get the real dotnet root
            var info = new FileInfo(candidate);
            string resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
            return Path.GetDirectoryName(resolved);
        }

        return null;
    }

    /// <summary>
    /// Get the well-known default install location for the current platform.
    /// </summary>
    static string GetDefaultInstallLocation()
    {
        if (OperatingSystem.IsMacOS())
            return "/usr/local/share/dotnet";

        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

        // Linux and other Unix
        return "/usr/share/dotnet";
    }
}
