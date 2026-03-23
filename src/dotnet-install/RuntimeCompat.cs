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
        try
        {
            if (!HostFxr.IsLoaded) return [];
            var info = HostFxr.GetEnvironmentInfo();
            return [.. info.Frameworks];
        }
        catch
        {
            return [];
        }
    }

    // ---- Framework resolution ----

    static bool TryResolveFrameworks(string runtimeConfigPath)
    {
        try
        {
            if (!HostFxr.IsLoaded) return false;

            // Use the low-level callback approach: hostfxr only invokes the callback
            // when the config is valid and frameworks are resolved. Malformed JSON
            // returns rc == 0 but skips the callback.
            var gcHandle = GCHandle.Alloc(new StrongBox<bool>(false));
            try
            {
                int rc = HostFxr.ResolveFrameworksForRuntimeConfig(
                    runtimeConfigPath, 0, &OnResolveCheck, (nint)gcHandle);
                return rc == 0 && ((StrongBox<bool>)gcHandle.Target!).Value;
            }
            finally
            {
                gcHandle.Free();
            }
        }
        catch
        {
            return false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnResolveCheck(nint resultPtr, nint context)
    {
        ((StrongBox<bool>)GCHandle.FromIntPtr(context).Target!).Value = true;
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
                return new(fw.Name, fw.Version, "");
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
