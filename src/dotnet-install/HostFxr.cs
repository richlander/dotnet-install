using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Provides access to all hostfxr native APIs.
/// Uses LibraryImport with a DLL import resolver to discover hostfxr via <see cref="NetHost"/>.
/// </summary>
static unsafe partial class HostFxr
{
    private const string LibName = "hostfxr";

    private static string? s_dotnetRoot;
    private static bool s_loaded;

    static HostFxr()
    {
        try
        {
            var discovery = NetHost.Discover();
            s_dotnetRoot = discovery.DotnetRoot;

            if (discovery.HostFxrPath is not null && NativeLibrary.TryLoad(discovery.HostFxrPath, out nint handle))
            {
                s_loaded = true;
                NativeLibrary.SetDllImportResolver(
                    typeof(HostFxr).Assembly,
                    (name, assembly, searchPath) => name == LibName ? handle : 0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HostFxr: failed to load hostfxr: {ex.Message}");
        }
    }

    /// <summary>The discovered dotnet root directory.</summary>
    internal static string? DotnetRoot => s_dotnetRoot;

    /// <summary>Whether hostfxr was successfully loaded.</summary>
    internal static bool IsLoaded => s_loaded;

    /// <summary>
    /// Discover the hostfxr library using the nethost discovery algorithm.
    /// </summary>
    internal static DiscoveryResult Discover(string? dotnetRoot = null) => NetHost.Discover(dotnetRoot);

    // ========================================================================
    // Raw API wrappers — all 18 hostfxr exports via LibraryImport
    // ========================================================================

    // 1. hostfxr_main
    [LibraryImport(LibName, EntryPoint = "hostfxr_main")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int HostFxrMain(int argc, nint argv);

    // 2. hostfxr_main_startupinfo
    [LibraryImport(LibName, EntryPoint = "hostfxr_main_startupinfo",
        StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PlatformStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int MainStartupInfo(int argc, nint argv,
        string hostPath, string dotnetRoot, string appPath);

    // 3. hostfxr_main_bundle_startupinfo
    [LibraryImport(LibName, EntryPoint = "hostfxr_main_bundle_startupinfo",
        StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PlatformStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int MainBundleStartupInfo(int argc, nint argv,
        string hostPath, string dotnetRoot, string appPath, long bundleHeaderOffset);

    // 4. hostfxr_resolve_sdk (obsolete — use ResolveSdk2)
    [Obsolete("Use ResolveSdk2 instead")]
    [LibraryImport(LibName, EntryPoint = "hostfxr_resolve_sdk")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ResolveSdk(nint exeDir, nint workingDir, nint buffer, int bufferSize);

    // 5. hostfxr_resolve_sdk2
    [LibraryImport(LibName, EntryPoint = "hostfxr_resolve_sdk2",
        StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PlatformStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ResolveSdk2(string? exeDir, string? workingDir, int flags,
        delegate* unmanaged[Cdecl]<int, nint, void> result);

    // 6. hostfxr_get_available_sdks
    [LibraryImport(LibName, EntryPoint = "hostfxr_get_available_sdks",
        StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PlatformStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetAvailableSdks(string? exeDir,
        delegate* unmanaged[Cdecl]<int, nint, void> result);

    // 7. hostfxr_get_dotnet_environment_info
    [LibraryImport(LibName, EntryPoint = "hostfxr_get_dotnet_environment_info",
        StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PlatformStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetDotnetEnvironmentInfo(string? dotnetRoot, nint reserved,
        delegate* unmanaged[Cdecl]<nint, nint, void> result, nint resultContext);

    // 8. hostfxr_get_native_search_directories
    [LibraryImport(LibName, EntryPoint = "hostfxr_get_native_search_directories")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetNativeSearchDirectories(int argc, nint argv,
        nint buffer, int bufferSize, int* requiredBufferSize);

    // 9. hostfxr_set_error_writer
    [LibraryImport(LibName, EntryPoint = "hostfxr_set_error_writer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint SetErrorWriter(delegate* unmanaged[Cdecl]<nint, void> errorWriter);

    // 10. hostfxr_initialize_for_dotnet_command_line
    [LibraryImport(LibName, EntryPoint = "hostfxr_initialize_for_dotnet_command_line")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int InitializeForDotnetCommandLine(int argc, nint argv,
        HostFxrInitializeParameters* parameters, nint* hostContextHandle);

    // 11. hostfxr_initialize_for_runtime_config
    [LibraryImport(LibName, EntryPoint = "hostfxr_initialize_for_runtime_config")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int InitializeForRuntimeConfig(nint runtimeConfigPath,
        HostFxrInitializeParameters* parameters, nint* hostContextHandle);

    // 12. hostfxr_resolve_frameworks_for_runtime_config
    [LibraryImport(LibName, EntryPoint = "hostfxr_resolve_frameworks_for_runtime_config",
        StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(PlatformStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ResolveFrameworksForRuntimeConfig(string runtimeConfigPath, nint parameters,
        delegate* unmanaged[Cdecl]<nint, nint, void> callback, nint resultContext);

    // 13. hostfxr_run_app
    [LibraryImport(LibName, EntryPoint = "hostfxr_run_app")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int RunApp(nint hostContextHandle);

    // 14. hostfxr_get_runtime_delegate
    [LibraryImport(LibName, EntryPoint = "hostfxr_get_runtime_delegate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetRuntimeDelegate(nint hostContextHandle, int type, nint* @delegate);

    // 15. hostfxr_get_runtime_property_value
    [LibraryImport(LibName, EntryPoint = "hostfxr_get_runtime_property_value")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetRuntimePropertyValue(nint hostContextHandle, nint name, nint* value);

    // 16. hostfxr_set_runtime_property_value
    [LibraryImport(LibName, EntryPoint = "hostfxr_set_runtime_property_value")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SetRuntimePropertyValue(nint hostContextHandle, nint name, nint value);

    // 17. hostfxr_get_runtime_properties
    [LibraryImport(LibName, EntryPoint = "hostfxr_get_runtime_properties")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetRuntimeProperties(nint hostContextHandle,
        nuint* count, nint* keys, nint* values);

    // 18. hostfxr_close
    [LibraryImport(LibName, EntryPoint = "hostfxr_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Close(nint hostContextHandle);

    // ========================================================================
    // High-level managed APIs
    // ========================================================================

    /// <summary>
    /// Get comprehensive .NET environment info (SDKs, runtimes, hostfxr version).
    /// </summary>
    internal static EnvironmentInfo GetEnvironmentInfo(string? dotnetRoot = null)
    {
        ThrowIfNotLoaded();
        dotnetRoot ??= s_dotnetRoot;

        var handle = GCHandle.Alloc(new StrongBox<EnvironmentInfo?>(null));
        try
        {
            int rc = GetDotnetEnvironmentInfo(dotnetRoot, 0, &OnEnvironmentInfo, (nint)handle);
            var result = ((StrongBox<EnvironmentInfo?>)handle.Target!).Value ?? new EnvironmentInfo();
            result.StatusCode = rc;
            return result;
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnEnvironmentInfo(nint infoPtr, nint context)
    {
        ref var info = ref Unsafe.AsRef<DotnetEnvironmentInfo>((void*)infoPtr);
        var box = (StrongBox<EnvironmentInfo?>)GCHandle.FromIntPtr(context).Target!;

        var sdks = new SdkInfo[(int)info.SdkCount];
        for (int i = 0; i < sdks.Length; i++)
        {
            ref var sdk = ref info.Sdks[i];
            sdks[i] = new SdkInfo(sdk.Version, sdk.Path);
        }

        var frameworks = new FrameworkInfo[(int)info.FrameworkCount];
        for (int i = 0; i < frameworks.Length; i++)
        {
            ref var fw = ref info.Frameworks[i];
            frameworks[i] = new FrameworkInfo(fw.Name, fw.Version, fw.Path);
        }

        box.Value = new EnvironmentInfo
        {
            HostFxrVersion = info.HostFxrVersion,
            HostFxrCommitHash = info.HostFxrCommitHash,
            Sdks = sdks,
            Frameworks = frameworks,
        };
    }

    /// <summary>
    /// Resolve SDK location and global.json info.
    /// </summary>
    [ThreadStatic]
    private static SdkResolutionResult? t_sdkResult;

    internal static SdkResolutionResult ResolveSdkInfo(string? exeDir = null, string? workingDir = null,
        ResolveSdk2Flags flags = ResolveSdk2Flags.None)
    {
        ThrowIfNotLoaded();

        var result = new SdkResolutionResult();
        t_sdkResult = result;

        try
        {
            result.StatusCode = ResolveSdk2(exeDir, workingDir, (int)flags, &OnResolveSdk2Result);
        }
        finally
        {
            t_sdkResult = null;
        }

        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnResolveSdk2Result(int key, nint value)
    {
        var result = t_sdkResult;
        if (result is null) return;

        string? str = PlatformStringMarshaller.ConvertToManaged(value);
        switch ((ResolveSdk2ResultKey)key)
        {
            case ResolveSdk2ResultKey.ResolvedSdkDir:
                result.ResolvedSdkDir = str;
                break;
            case ResolveSdk2ResultKey.GlobalJsonPath:
                result.GlobalJsonPath = str;
                break;
            case ResolveSdk2ResultKey.RequestedVersion:
                result.RequestedVersion = str;
                break;
            case ResolveSdk2ResultKey.GlobalJsonState:
                result.GlobalJsonState = str switch
                {
                    "not_found" => GlobalJsonState.NotFound,
                    "valid" => GlobalJsonState.Valid,
                    "__invalid_data_no_fallback" => GlobalJsonState.InvalidDataNoFallback,
                    "invalid_json" => GlobalJsonState.InvalidJson,
                    _ => GlobalJsonState.Unknown,
                };
                break;
        }
    }

    /// <summary>
    /// Get all available SDK directories.
    /// </summary>
    [ThreadStatic]
    private static List<string>? t_sdkDirs;

    internal static AvailableSdksResult GetAvailableSdkDirs(string? exeDir = null)
    {
        ThrowIfNotLoaded();

        t_sdkDirs = [];
        try
        {
            int rc = GetAvailableSdks(exeDir, &OnAvailableSdks);
            return new AvailableSdksResult { StatusCode = rc, SdkDirs = t_sdkDirs.ToArray() };
        }
        finally
        {
            t_sdkDirs = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAvailableSdks(int sdkCount, nint sdkDirs)
    {
        var list = t_sdkDirs;
        if (list is null) return;

        for (int i = 0; i < sdkCount; i++)
        {
            nint ptr = ((nint*)sdkDirs)[i];
            string? dir = PlatformStringMarshaller.ConvertToManaged(ptr);
            if (dir is not null)
                list.Add(dir);
        }
    }

    /// <summary>
    /// Resolve frameworks for a runtime config file.
    /// </summary>
    internal static FrameworkResolutionResult ResolveFrameworks(string runtimeConfigPath, string? dotnetRoot = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeConfigPath);
        ThrowIfNotLoaded();

        var result = new FrameworkResolutionResult();
        var gcHandle = GCHandle.Alloc(new StrongBox<FrameworkResolutionResult?>(null));

        nint rootPtr = dotnetRoot is not null ? PlatformStringMarshaller.ConvertToUnmanaged(dotnetRoot) : 0;

        nint paramsPtr = 0;
        HostFxrInitializeParameters initParams = default;
        if (rootPtr != 0)
        {
            initParams = HostFxrInitializeParameters.Create(0, rootPtr);
            paramsPtr = (nint)(&initParams);
        }

        try
        {
            int rc = ResolveFrameworksForRuntimeConfig(runtimeConfigPath, paramsPtr,
                &OnResolveFrameworks, (nint)gcHandle);
            result = ((StrongBox<FrameworkResolutionResult?>)gcHandle.Target!).Value ?? result;
            result.StatusCode = rc;
        }
        finally
        {
            PlatformStringMarshaller.Free(rootPtr);
            gcHandle.Free();
        }

        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnResolveFrameworks(nint resultPtr, nint context)
    {
        ref var r = ref Unsafe.AsRef<ResolveFrameworksResult>((void*)resultPtr);
        var box = (StrongBox<FrameworkResolutionResult?>)GCHandle.FromIntPtr(context).Target!;

        var resolved = new FrameworkEntry[(int)r.ResolvedCount];
        for (int i = 0; i < resolved.Length; i++)
        {
            ref var fw = ref r.ResolvedFrameworks[i];
            resolved[i] = new FrameworkEntry(fw.Name, fw.RequestedVersion, fw.ResolvedVersion, fw.ResolvedPath);
        }

        var unresolved = new FrameworkEntry[(int)r.UnresolvedCount];
        for (int i = 0; i < unresolved.Length; i++)
        {
            ref var fw = ref r.UnresolvedFrameworks[i];
            unresolved[i] = new FrameworkEntry(fw.Name, fw.RequestedVersion, fw.ResolvedVersion, fw.ResolvedPath);
        }

        box.Value = new FrameworkResolutionResult { Resolved = resolved, Unresolved = unresolved };
    }

    /// <summary>
    /// Initialize a host context for a runtime config, returning a managed handle.
    /// </summary>
    internal static HostContextHandle InitializeForRuntimeConfig(string runtimeConfigPath,
        string? hostPath = null, string? dotnetRoot = null)
    {
        ThrowIfNotLoaded();
        ArgumentNullException.ThrowIfNull(runtimeConfigPath);

        nint configPtr = PlatformStringMarshaller.ConvertToUnmanaged(runtimeConfigPath);
        nint hostPathPtr = hostPath is not null ? PlatformStringMarshaller.ConvertToUnmanaged(hostPath) : 0;
        nint dotnetRootPtr = dotnetRoot is not null ? PlatformStringMarshaller.ConvertToUnmanaged(dotnetRoot) : 0;

        try
        {
            HostFxrInitializeParameters* paramsPtr = null;
            HostFxrInitializeParameters initParams = default;
            if (hostPathPtr != 0 || dotnetRootPtr != 0)
            {
                initParams = HostFxrInitializeParameters.Create(hostPathPtr, dotnetRootPtr);
                paramsPtr = &initParams;
            }

            nint handle = 0;
            int rc = InitializeForRuntimeConfig(configPtr, paramsPtr, &handle);
            return new HostContextHandle(handle, rc);
        }
        finally
        {
            PlatformStringMarshaller.Free(configPtr);
            PlatformStringMarshaller.Free(hostPathPtr);
            PlatformStringMarshaller.Free(dotnetRootPtr);
        }
    }

    /// <summary>
    /// Initialize a host context for dotnet command line execution, returning a managed handle.
    /// </summary>
    internal static HostContextHandle InitializeForCommandLine(string[] args,
        string? hostPath = null, string? dotnetRoot = null)
    {
        ThrowIfNotLoaded();
        ArgumentNullException.ThrowIfNull(args);

        nint hostPathPtr = hostPath is not null ? PlatformStringMarshaller.ConvertToUnmanaged(hostPath) : 0;
        nint dotnetRootPtr = dotnetRoot is not null ? PlatformStringMarshaller.ConvertToUnmanaged(dotnetRoot) : 0;

        int argc = args.Length;
        nint* argv = stackalloc nint[argc];
        for (int i = 0; i < argc; i++)
            argv[i] = PlatformStringMarshaller.ConvertToUnmanaged(args[i]);

        try
        {
            HostFxrInitializeParameters* paramsPtr = null;
            HostFxrInitializeParameters initParams = default;
            if (hostPathPtr != 0 || dotnetRootPtr != 0)
            {
                initParams = HostFxrInitializeParameters.Create(hostPathPtr, dotnetRootPtr);
                paramsPtr = &initParams;
            }

            nint handle = 0;
            int rc = InitializeForDotnetCommandLine(argc, (nint)argv, paramsPtr, &handle);
            return new HostContextHandle(handle, rc);
        }
        finally
        {
            for (int i = 0; i < argc; i++)
                PlatformStringMarshaller.Free(argv[i]);
            PlatformStringMarshaller.Free(hostPathPtr);
            PlatformStringMarshaller.Free(dotnetRootPtr);
        }
    }

    /// <summary>
    /// Begin capturing hostfxr error messages. Dispose to restore the previous writer.
    /// </summary>
    internal static ErrorCapture CaptureErrors() => new();

    // ---- Unix process replacement ----

    [LibraryImport("libc", EntryPoint = "execvp", SetLastError = true)]
    internal static partial int ExecVP(nint file, nint* argv);

    // ========================================================================
    // Internal helpers
    // ========================================================================

    private static void ThrowIfNotLoaded()
    {
        if (!s_loaded)
            throw new DllNotFoundException(
                "hostfxr could not be loaded. Ensure .NET is installed and DOTNET_ROOT is set.");
    }
}
