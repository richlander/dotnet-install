using System.CommandLine;

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

// System.CommandLine's default handler prints a stack trace for anything that
// escapes. That is right for a bug and wrong for a manifest the user can fix,
// so handle the expected case here and leave the rest looking as it did.
var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };

try
{
    return await rootCommand.Parse(args).InvokeAsync(invocation);
}
catch (ManifestException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Unhandled exception: {e}");
    return 1;
}
