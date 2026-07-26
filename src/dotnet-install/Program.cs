// --- Version (early, before System.CommandLine) ---

if (args is ["--version"])
{
    Console.WriteLine($"dotnet-install {typeof(Installer).Assembly.GetName().Version}");
    return 0;
}

// --- System.CommandLine dispatch ---

// Guard against System.CommandLine swallowing a leading positional path whose
// file name matches the command name (see CommandLineBuilder.NormalizeArgs).
args = CommandLineBuilder.NormalizeArgs(args);

var rootCommand = CommandLineBuilder.CreateRootCommand();
return await rootCommand.Parse(args).InvokeAsync();
