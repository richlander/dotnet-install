/// <summary>
/// View and update dotnet-install configuration.
/// </summary>
static class ConfigCommand
{
    public static int Run(string installDir, string? key, string? value)
    {
        var config = UserConfig.Read(installDir);

        // No args: show all settings
        if (key is null)
        {
            if (UserConfig.Keys.Length == 0)
            {
                Console.WriteLine("No configuration settings available.");
                return 0;
            }

            foreach (var (k, description) in UserConfig.Keys)
            {
                string v = config.Get(k) ?? "";
                Console.WriteLine($"{k} = {v}");
            }

            return 0;
        }

        // Key only: show single value
        if (value is null)
        {
            string? current = config.Get(key);
            if (current is null)
            {
                Console.Error.WriteLine($"error: unknown config key '{key}'");
                Console.Error.WriteLine();
                PrintAvailableKeys();
                return 1;
            }

            Console.WriteLine(current);
            return 0;
        }

        // Key + value: set
        if (!config.Set(key, value))
        {
            Console.Error.WriteLine($"error: unknown key or invalid value '{key} {value}'");
            Console.Error.WriteLine();
            PrintAvailableKeys();
            return 1;
        }

        UserConfig.Write(installDir, config);
        Console.WriteLine($"{key} = {value}");
        return 0;
    }

    static void PrintAvailableKeys()
    {
        Console.Error.WriteLine("Available keys:");
        foreach (var (k, description) in UserConfig.Keys)
        {
            Console.Error.WriteLine($"  {k,-25} {description}");
        }
    }
}
