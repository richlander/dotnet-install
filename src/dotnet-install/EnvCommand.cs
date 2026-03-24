/// <summary>
/// Print dotnet-install environment information (like `cargo env` / `go env`).
/// </summary>
static class EnvCommand
{
    public static int Run(string installDir)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // DOTNET_TOOL_BIN
        string? envHome = Environment.GetEnvironmentVariable(ShellConfig.EnvVar);
        string effectiveHome = envHome ?? installDir;

        Console.WriteLine($"{ShellConfig.EnvVar}={Quote(effectiveHome)}{(envHome is null ? "  # default" : "")}");
        Console.WriteLine($"DOTNET_INSTALL_PATH={Quote(effectiveHome)}");

        // Show dotnet-install binary info
        string binary = Path.Combine(effectiveHome, "dotnet-install");
        if (File.Exists(binary))
        {
            var info = new FileInfo(binary);
            Console.WriteLine($"DOTNET_INSTALL_BIN={Quote(binary)}");
            Console.WriteLine($"DOTNET_INSTALL_VERSION={Quote(GetVersion())}");
        }
        else
        {
            Console.WriteLine($"DOTNET_INSTALL_BIN=  # not found");
        }

        // PATH status
        bool onPath = ShellConfig.IsOnPath(effectiveHome);
        Console.WriteLine($"DOTNET_INSTALL_ON_PATH={onPath.ToString().ToLowerInvariant()}");

        return 0;
    }

    static string Quote(string value) => $"\"{value}\"";

    static string GetVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version;
        return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
    }
}
