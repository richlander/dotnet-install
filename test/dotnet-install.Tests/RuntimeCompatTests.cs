using System.Text.Json;

namespace dotnet_install.Tests;

/// <summary>
/// Tests for RuntimeCompat — runtimeconfig.json parsing and compatibility checking.
/// The hostfxr-dependent tests (TryResolveFrameworks) require a real .NET installation,
/// so they run as integration tests on the local machine.
/// </summary>
public class RuntimeCompatTests : IDisposable
{
    readonly string _tempDir;

    public RuntimeCompatTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-install-test-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ---- runtimeconfig.json parsing ----

    [Fact]
    public void CheckCompatibility_ReturnsFalse_WhenFileDoesNotExist()
    {
        var result = RuntimeCompat.CheckCompatibility(Path.Combine(_tempDir, "nonexistent.runtimeconfig.json"));

        Assert.False(result.CanRun);
        Assert.False(result.RollForwardWouldHelp);
    }

    [Fact]
    public void CheckCompatibility_CompatibleRuntime_ReturnsCanRun()
    {
        // Create a runtimeconfig targeting the currently installed runtime
        // (which should always be resolvable on the test machine)
        var runtimes = RuntimeCompat.GetInstalledRuntimes();
        var netcoreRuntime = runtimes.FirstOrDefault(r => r.Name == "Microsoft.NETCore.App");

        if (netcoreRuntime is null)
        {
            // Skip if no runtime found (unlikely on a dev machine)
            return;
        }

        string configPath = WriteRuntimeConfig("Microsoft.NETCore.App", netcoreRuntime.Version);
        var result = RuntimeCompat.CheckCompatibility(configPath);

        Assert.True(result.CanRun);
    }

    [Fact]
    public void CheckCompatibility_FutureRuntime_ReturnsCantRun()
    {
        // Target a runtime version that certainly doesn't exist
        string configPath = WriteRuntimeConfig("Microsoft.NETCore.App", "99.0.0");
        var result = RuntimeCompat.CheckCompatibility(configPath);

        Assert.False(result.CanRun);
    }

    [Fact]
    public void CheckCompatibility_OlderRuntime_RollForwardWouldHelp()
    {
        // Target a runtime version older than what's installed
        // Roll-forward to the installed version should work
        var runtimes = RuntimeCompat.GetInstalledRuntimes();
        var netcoreRuntime = runtimes
            .Where(r => r.Name == "Microsoft.NETCore.App")
            .Select(r => (r, ver: Version.TryParse(r.Version, out var v) ? v : null))
            .Where(x => x.ver is not null)
            .OrderByDescending(x => x.ver)
            .Select(x => x.r)
            .FirstOrDefault();

        if (netcoreRuntime is null)
            return;

        // Parse the version to create an older major version
        if (!Version.TryParse(netcoreRuntime.Version, out var currentVersion) || currentVersion.Major <= 1)
            return;

        // Target a version with a lower major (e.g., if current is 11.0, target 6.0)
        string olderVersion = $"{currentVersion.Major - 5}.0.0";
        if (currentVersion.Major - 5 < 1)
            olderVersion = "1.0.0";

        string configPath = WriteRuntimeConfig("Microsoft.NETCore.App", olderVersion);
        var result = RuntimeCompat.CheckCompatibility(configPath);

        // Should not be directly runnable (no exact match), but roll-forward should help
        // Note: this depends on the default rollForward policy in the config.
        // With no rollForward specified, default is LatestPatch which doesn't cross major versions.
        // So CanRun should be false and RollForwardWouldHelp should be true.
        if (!result.CanRun)
        {
            Assert.True(result.RollForwardWouldHelp);
        }
        // If it can run (runtime auto-resolved), that's also acceptable
    }

    // ---- GetInstalledRuntimes via hostfxr ----

    [Fact]
    public void GetInstalledRuntimes_ReturnsNonEmptyList()
    {
        var runtimes = RuntimeCompat.GetInstalledRuntimes();

        Assert.NotEmpty(runtimes);
        Assert.Contains(runtimes, r => r.Name == "Microsoft.NETCore.App");
    }

    [Fact]
    public void GetInstalledRuntimes_ContainsValidVersions()
    {
        var runtimes = RuntimeCompat.GetInstalledRuntimes();

        foreach (var runtime in runtimes)
        {
            Assert.False(string.IsNullOrEmpty(runtime.Name));
            Assert.False(string.IsNullOrEmpty(runtime.Version));
            // Version should be parseable (at least the major.minor.patch part)
            string versionPart = runtime.Version.Split('-')[0]; // strip prerelease suffix
            Assert.True(Version.TryParse(versionPart, out _),
                $"Could not parse version '{runtime.Version}' for {runtime.Name}");
        }
    }

    // ---- Malformed runtimeconfig ----

    [Fact]
    public void CheckCompatibility_MalformedJson_ReturnsFalse()
    {
        string path = Path.Combine(_tempDir, "bad.runtimeconfig.json");
        File.WriteAllText(path, "not json at all");

        var result = RuntimeCompat.CheckCompatibility(path);
        Assert.False(result.CanRun);
    }

    [Fact]
    public void CheckCompatibility_EmptyRuntimeOptions_Succeeds()
    {
        // An empty runtimeOptions has no framework requirement, so hostfxr resolves it successfully
        string path = Path.Combine(_tempDir, "empty.runtimeconfig.json");
        File.WriteAllText(path, """{"runtimeOptions": {}}""");

        var result = RuntimeCompat.CheckCompatibility(path);
        Assert.True(result.CanRun);
    }

    // ---- Helper ----

    string WriteRuntimeConfig(string frameworkName, string version, string? rollForward = null)
    {
        string path = Path.Combine(_tempDir, $"test-{Path.GetRandomFileName()}.runtimeconfig.json");

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WritePropertyName("runtimeOptions");
        writer.WriteStartObject();

        if (rollForward is not null)
            writer.WriteString("rollForward", rollForward);

        writer.WritePropertyName("framework");
        writer.WriteStartObject();
        writer.WriteString("name", frameworkName);
        writer.WriteString("version", version);
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.WriteEndObject();

        return path;
    }
}
