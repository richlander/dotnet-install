using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// User-level configuration for dotnet-install, stored in the install directory.
/// </summary>
class UserConfig
{
    const string FileName = ".config.json";

    /// <summary>
    /// When true, `doctor` will drain `dotnet tool install -g` tools
    /// by reinstalling them via dotnet-install and removing the dotnet tool version.
    /// </summary>
    [JsonPropertyName("manage-global-tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ManageGlobalTools { get; set; }

    /// <summary>
    /// When true, suppress the PATH tip shown in ephemeral shells
    /// where the install dir is configured but not yet active.
    /// </summary>
    [JsonPropertyName("tip.quiet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TipQuiet { get; set; }

    static string GetPath(string installDir) =>
        Path.Combine(installDir, FileName);

    internal static UserConfig Read(string installDir)
    {
        string path = GetPath(installDir);
        if (!File.Exists(path)) return new();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, UserConfigContext.Default.UserConfig) ?? new();
        }
        catch
        {
            return new();
        }
    }

    internal static void Write(string installDir, UserConfig config)
    {
        Directory.CreateDirectory(installDir);
        string path = GetPath(installDir);
        string json = JsonSerializer.Serialize(config, UserConfigContext.Default.UserConfig);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Get a config value by key name. Returns null if key is unknown.
    /// </summary>
    internal string? Get(string key) => key switch
    {
        "manage-global-tools" => ManageGlobalTools.ToString().ToLowerInvariant(),
        "tip.quiet" => TipQuiet.ToString().ToLowerInvariant(),
        _ => null
    };

    /// <summary>
    /// Set a config value by key name. Returns false if key is unknown or value is invalid.
    /// </summary>
    internal bool Set(string key, string value)
    {
        if (!bool.TryParse(value, out bool boolValue))
            return false;

        switch (key)
        {
            case "manage-global-tools":
                ManageGlobalTools = boolValue;
                return true;
            case "tip.quiet":
                TipQuiet = boolValue;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// All known config keys with descriptions.
    /// </summary>
    internal static readonly (string Key, string Description)[] Keys =
    [
        ("manage-global-tools", "Drain dotnet global tools into dotnet-install during doctor"),
        ("tip.quiet", "Suppress PATH tips in ephemeral shells")
    ];
}

[JsonSerializableAttribute(typeof(UserConfig))]
[JsonSourceGenerationOptions(WriteIndented = true)]
partial class UserConfigContext : JsonSerializerContext { }
