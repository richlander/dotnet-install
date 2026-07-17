// --- Version (early, before System.CommandLine) ---

if (args is ["--version"])
{
    Console.WriteLine($"dotnet-install {typeof(Installer).Assembly.GetName().Version}");
    return 0;
}

// --- System.CommandLine dispatch ---

var rootCommand = CommandLineBuilder.CreateRootCommand();
return await rootCommand.Parse(args).InvokeAsync();
