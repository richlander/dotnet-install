using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Runtime compatibility checking using hostfxr native APIs.
/// Determines if a managed tool can run with installed runtimes and
/// whether roll-forward would help.
/// </summary>
static unsafe class RuntimeCompat
{
    internal record CompatResult(
        bool CanRun,
        bool RollForwardWouldHelp,
        string? RequiredFramework,
        string? RequiredVersion);

    internal record FrameworkInfo(string Name, string Version);

    /// <summary>
    /// Check if a managed tool's runtimeconfig.json can be satisfied by installed runtimes.
    /// If not, determines whether --allow-roll-forward would help.
    /// </summary>
    internal static CompatResult CheckCompatibility(string runtimeConfigPath)
    {
        if (!File.Exists(runtimeConfigPath))
            return new(false, false, null, null);

        // 1. Try resolving frameworks as-is
        if (TryResolveFrameworks(runtimeConfigPath))
            return new(true, false, null, null);

        // 2. Parse runtimeconfig.json for error reporting
        var required = ParseRuntimeConfig(runtimeConfigPath);

        // 3. Try resolving with roll-forward enabled (temp modified config)
        bool rollForwardHelps = TryResolveWithRollForward(runtimeConfigPath);

        return new(
            CanRun: false,
            RollForwardWouldHelp: rollForwardHelps,
            RequiredFramework: required?.Name,
            RequiredVersion: required?.Version);
    }

    /// <summary>
    /// Enumerate all installed .NET runtimes via hostfxr.
    /// </summary>
    internal static List<FrameworkInfo> GetInstalledRuntimes()
    {
        var results = new List<FrameworkInfo>();
        var handle = GCHandle.Alloc(results);

        try
        {
            string? dotnetRoot = HostFxr.DotnetRoot;
            nint rootPtr = dotnetRoot is not null ? HostFxr.MarshalString(dotnetRoot) : 0;

            try
            {
                HostFxr.GetDotnetEnvironmentInfo(
                    rootPtr, 0, &OnEnvironmentInfo, (nint)handle);
            }
            finally
            {
                HostFxr.FreeString(rootPtr);
            }
        }
        catch
        {
            // hostfxr unavailable
        }
        finally
        {
            handle.Free();
        }

        return results;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnEnvironmentInfo(nint infoPtr, nint context)
    {
        var results = (List<FrameworkInfo>)GCHandle.FromIntPtr(context).Target!;
        ref var info = ref Unsafe.AsRef<HostFxr.DotnetEnvironmentInfo>((void*)infoPtr);

        for (nuint i = 0; i < info.FrameworkCount; i++)
        {
            ref var fw = ref Unsafe.Add(
                ref Unsafe.AsRef<HostFxr.DotnetEnvironmentFrameworkInfo>((void*)info.Frameworks), (int)i);

            string? name = HostFxr.PtrToString(fw.Name);
            string? version = HostFxr.PtrToString(fw.Version);

            if (name is not null && version is not null)
                results.Add(new(name, version));
        }
    }

    // ---- Framework resolution ----

    static bool TryResolveFrameworks(string runtimeConfigPath)
    {
        try
        {
            nint pathPtr = HostFxr.MarshalString(runtimeConfigPath);
            bool resolved = false;
            var handle = GCHandle.Alloc(new StrongBox<bool>(false));

            try
            {
                int rc = HostFxr.ResolveFrameworksForRuntimeConfig(
                    pathPtr, 0, &OnResolveFrameworks, (nint)handle);

                var box = (StrongBox<bool>)GCHandle.FromIntPtr((nint)handle).Target!;
                resolved = rc == 0 && box.Value;
            }
            finally
            {
                HostFxr.FreeString(pathPtr);
                handle.Free();
            }

            return resolved;
        }
        catch
        {
            return false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnResolveFrameworks(nint resultPtr, nint context)
    {
        var box = (StrongBox<bool>)GCHandle.FromIntPtr(context).Target!;
        box.Value = true; // callback invoked = resolution succeeded
    }

    static bool TryResolveWithRollForward(string runtimeConfigPath)
    {
        string? tempPath = null;
        try
        {
            // Create a temp copy with rollForward set to LatestMajor
            string json = File.ReadAllText(runtimeConfigPath);
            var doc = JsonSerializer.Deserialize(json, RuntimeConfigContext.Default.JsonDocument);
            if (doc is null) return false;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "runtimeOptions")
                    {
                        writer.WritePropertyName("runtimeOptions");
                        writer.WriteStartObject();
                        bool wroteRollForward = false;
                        foreach (var inner in prop.Value.EnumerateObject())
                        {
                            if (inner.Name == "rollForward")
                            {
                                writer.WriteString("rollForward", "LatestMajor");
                                wroteRollForward = true;
                            }
                            else
                            {
                                inner.WriteTo(writer);
                            }
                        }
                        if (!wroteRollForward)
                            writer.WriteString("rollForward", "LatestMajor");
                        writer.WriteEndObject();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            tempPath = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Path.GetRandomFileName()}.runtimeconfig.json");
            File.WriteAllBytes(tempPath, stream.ToArray());

            return TryResolveFrameworks(tempPath);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    // ---- runtimeconfig.json parsing ----

    static FrameworkInfo? ParseRuntimeConfig(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize(json, RuntimeConfigContext.Default.RuntimeConfig);
            var fw = config?.RuntimeOptions?.Framework;
            if (fw?.Name is not null && fw.Version is not null)
                return new(fw.Name, fw.Version);
            return null;
        }
        catch
        {
            return null;
        }
    }
}

// ---- JSON models for runtimeconfig.json ----

class RuntimeConfig
{
    [JsonPropertyName("runtimeOptions")]
    public RuntimeOptions? RuntimeOptions { get; set; }
}

class RuntimeOptions
{
    [JsonPropertyName("tfm")]
    public string? Tfm { get; set; }

    [JsonPropertyName("rollForward")]
    public string? RollForward { get; set; }

    [JsonPropertyName("framework")]
    public FrameworkReference? Framework { get; set; }
}

class FrameworkReference
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

[JsonSerializable(typeof(RuntimeConfig))]
[JsonSerializable(typeof(JsonDocument))]
partial class RuntimeConfigContext : JsonSerializerContext { }
