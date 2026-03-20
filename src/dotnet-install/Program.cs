// --- Runtime host dispatch ---
// When invoked via a symlink (BusyBox model), the invocation name differs
// from "dotnet-install". Detect this and dispatch to the managed tool.

string invocationName = Path.GetFileNameWithoutExtension(
    Environment.GetCommandLineArgs()[0]);

if (invocationName != "dotnet-install" && !invocationName.StartsWith("dotnet-install."))
{
    return HostDispatch.Run(invocationName, args);
}

// --- Explicit host mode (Windows .cmd shims) ---

if (args is ["--host", _, ..])
{
    string toolName = args[1];
    string[] toolArgs = args[2..];
    return HostDispatch.Run(toolName, toolArgs);
}

// --- Version (early, before System.CommandLine) ---

if (args is ["--version"])
{
    Console.WriteLine($"dotnet-install {typeof(Installer).Assembly.GetName().Version}");
    return 0;
}

// --- System.CommandLine dispatch ---

var rootCommand = CommandLineBuilder.CreateRootCommand();
return await rootCommand.Parse(args).InvokeAsync();
