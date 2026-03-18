using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

/// <summary>
/// Low-level interop layer for hostfxr native APIs.
/// Uses manual function pointer resolution to handle NativeAOT correctly,
/// plus LibraryImport for system libraries (libc).
/// </summary>
static unsafe partial class HostFxr
{
    const string LibName = "hostfxr";

    static string? s_dotnetRoot;
    static bool s_initialized;
    static nint s_hostfxrHandle;

    static HostFxr()
    {
        // Load hostfxr manually — SetDllImportResolver doesn't work in NativeAOT
        EnsureInitialized();
        string? path = FindHostFxrPath();
        if (path is not null)
            NativeLibrary.TryLoad(path, out s_hostfxrHandle);
    }

    /// <summary>
    /// Wraps hostfxr_get_dotnet_environment_info via manual function pointer.
    /// </summary>
    internal static int GetDotnetEnvironmentInfo(
        nint dotnet_root, nint reserved,
        delegate* unmanaged[Cdecl]<nint, nint, void> result,
        nint result_context)
    {
        if (s_hostfxrHandle == 0)
            throw new DllNotFoundException("hostfxr not loaded");

        if (!NativeLibrary.TryGetExport(s_hostfxrHandle,
            "hostfxr_get_dotnet_environment_info", out nint fn))
            throw new EntryPointNotFoundException("hostfxr_get_dotnet_environment_info");

        return ((delegate* unmanaged[Cdecl]<nint, nint,
            delegate* unmanaged[Cdecl]<nint, nint, void>, nint, int>)fn)(
                dotnet_root, reserved, result, result_context);
    }

    /// <summary>
    /// Wraps hostfxr_resolve_frameworks_for_runtime_config via manual function pointer.
    /// </summary>
    internal static int ResolveFrameworksForRuntimeConfig(
        nint runtime_config_path, nint parameters,
        delegate* unmanaged[Cdecl]<nint, nint, void> callback,
        nint result_context)
    {
        if (s_hostfxrHandle == 0)
            throw new DllNotFoundException("hostfxr not loaded");

        if (!NativeLibrary.TryGetExport(s_hostfxrHandle,
            "hostfxr_resolve_frameworks_for_runtime_config", out nint fn))
            throw new EntryPointNotFoundException("hostfxr_resolve_frameworks_for_runtime_config");

        return ((delegate* unmanaged[Cdecl]<nint, nint,
            delegate* unmanaged[Cdecl]<nint, nint, void>, nint, int>)fn)(
                runtime_config_path, parameters, callback, result_context);
    }

    internal static string? DotnetRoot
    {
        get
        {
            EnsureInitialized();
            return s_dotnetRoot;
        }
    }

    static void EnsureInitialized()
    {
        if (s_initialized) return;
        s_initialized = true;

        s_dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (string.IsNullOrEmpty(s_dotnetRoot))
        {
            // Infer from the dotnet executable on PATH
            string dotnetExe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            string? pathVar = Environment.GetEnvironmentVariable("PATH");
            if (pathVar is not null)
            {
                char sep = OperatingSystem.IsWindows() ? ';' : ':';
                foreach (string dir in pathVar.Split(sep, StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = Path.Combine(dir, dotnetExe);
                    if (File.Exists(candidate))
                    {
                        // Resolve symlinks to get the real dotnet root
                        var info = new FileInfo(candidate);
                        string resolved = info.LinkTarget is not null
                            ? Path.GetFullPath(Path.Combine(dir, info.LinkTarget))
                            : candidate;
                        s_dotnetRoot = Path.GetDirectoryName(resolved);
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(s_dotnetRoot))
        {
            // Well-known default locations
            if (OperatingSystem.IsMacOS())
                s_dotnetRoot = "/usr/local/share/dotnet";
            else if (OperatingSystem.IsWindows())
                s_dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            else
                s_dotnetRoot = "/usr/share/dotnet";
        }
    }

    internal static string? FindHostFxrPath()
    {
        EnsureInitialized();
        if (s_dotnetRoot is null) return null;

        string fxrDir = Path.Combine(s_dotnetRoot, "host", "fxr");
        if (!Directory.Exists(fxrDir)) return null;

        // Find the highest versioned hostfxr
        // Use directory enumeration — Version.TryParse doesn't handle preview suffixes
        string? best = Directory.GetDirectories(fxrDir)
            .OrderByDescending(d => Path.GetFileName(d))
            .FirstOrDefault();

        if (best is null) return null;

        string libName = OperatingSystem.IsWindows() ? "hostfxr.dll" :
                         OperatingSystem.IsMacOS() ? "libhostfxr.dylib" :
                         "libhostfxr.so";

        string libPath = Path.Combine(best, libName);
        return File.Exists(libPath) ? libPath : null;
    }

    // ---- String marshalling helpers ----
    // hostfxr uses wchar_t on Windows (UTF-16) and char on Unix (UTF-8).

    internal static nint MarshalString(string s)
    {
        if (OperatingSystem.IsWindows())
            return Marshal.StringToHGlobalUni(s);
        return (nint)Utf8StringMarshaller.ConvertToUnmanaged(s);
    }

    internal static void FreeString(nint ptr)
    {
        if (ptr == 0) return;
        if (OperatingSystem.IsWindows())
            Marshal.FreeHGlobal(ptr);
        else
            Utf8StringMarshaller.Free((byte*)ptr);
    }

    internal static string? PtrToString(nint ptr)
    {
        if (ptr == 0) return null;
        return OperatingSystem.IsWindows()
            ? Marshal.PtrToStringUni(ptr)
            : Marshal.PtrToStringUTF8(ptr);
    }

    // ---- Struct definitions ----
    // String fields are nint (char_t*) — use PtrToString() to read.

    [StructLayout(LayoutKind.Sequential)]
    internal struct DotnetEnvironmentInfo
    {
        public nuint Size;
        public nint HostfxrVersion;
        public nint HostfxrCommitHash;
        public nuint SdkCount;
        public nint Sdks;
        public nuint FrameworkCount;
        public nint Frameworks;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DotnetEnvironmentFrameworkInfo
    {
        public nuint Size;
        public nint Name;
        public nint Version;
        public nint Path;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResolveFrameworksResult
    {
        public nuint Size;
        public nuint ResolvedCount;
        public nint ResolvedFrameworks;
        public nuint UnresolvedCount;
        public nint UnresolvedFrameworks;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FrameworkResult
    {
        public nuint Size;
        public nint Name;
        public nint RequestedVersion;
        public nint ResolvedVersion;
        public nint ResolvedPath;
    }

    // ---- Unix process replacement ----

    [LibraryImport("libc", EntryPoint = "execvp", SetLastError = true)]
    internal static partial int ExecVP(nint file, nint* argv);
}
