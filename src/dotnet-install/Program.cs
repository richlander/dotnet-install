// --- Route subcommands ---

if (args is ["list", ..])
    return ListCommand.Run(Installer.DefaultInstallDir);

if (args is ["remove", ..])
    return RemoveCommand.Run(Installer.DefaultInstallDir, args[1..]);

// --- Default: install ---

var options = ParseOptions(args);

if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

string installDir = options.OutputDir
    ?? (options.UseLocalBin ? Installer.LocalBinDir : Installer.DefaultInstallDir);

int result;

if (options.PackageSpec is not null)
{
    // Explicit: --package
    result = await Installer.InstallPackageAsync(options.PackageSpec, installDir);
}
else if (options.GitSpec is not null)
{
    // Explicit: --github, or confirmed via prompt
    result = GitSource.InstallFromGit(options.GitSpec, installDir, options.UseSsh, options.ProjectPath);
}
else if (options.UnresolvedArg is not null)
{
    // Positional arg that isn't a local path — prompt before reaching out to GitHub
    string arg = options.UnresolvedArg;
    string url = options.UseSsh
        ? $"git@github.com:{arg.Split('@')[0]}.git"
        : $"https://github.com/{arg.Split('@')[0]}";

    if (!Console.IsInputRedirected)
    {
        Console.Write($"  '{arg}' is not a local path. Clone from {url}? [Y/n] ");
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();

        if (key.Key == ConsoleKey.Escape || key.KeyChar is 'n' or 'N')
        {
            Console.WriteLine("  Cancelled.");
            return 1;
        }
    }
    else
    {
        Console.Error.WriteLine($"  '{arg}' is not a local path. Use --github to install from GitHub in non-interactive mode.");
        return 1;
    }

    result = GitSource.InstallFromGit(arg, installDir, options.UseSsh, options.ProjectPath);
}
else
{
    // Local project (path or cwd)
    string projectPath = options.ProjectPath ?? Directory.GetCurrentDirectory();
    string? projectFile = FindProjectFile(projectPath);

    if (projectFile is null)
    {
        Console.Error.WriteLine($"error: no project file found in '{projectPath}'");
        return 1;
    }

    result = Installer.Install(projectFile, installDir);
}

if (result == 0)
    ShellHint.PrintIfNeeded(installDir);

return result;

// ---- Argument parsing ----

static string? FindProjectFile(string path)
{
    if (File.Exists(path) && IsProjectFile(path))
        return Path.GetFullPath(path);

    if (Directory.Exists(path))
    {
        var projects = Directory.GetFiles(path, "*.*proj")
            .Where(IsProjectFile)
            .ToArray();

        if (projects.Length == 1)
            return projects[0];

        if (projects.Length > 1)
        {
            Console.Error.WriteLine($"error: multiple project files found in '{path}'. Specify one explicitly.");
            return null;
        }
    }

    return null;

    static bool IsProjectFile(string f) =>
        f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
}

static Options ParseOptions(string[] args)
{
    string? projectPath = null;
    string? outputDir = null;
    string? packageSpec = null;
    string? gitSpec = null;
    string? unresolvedArg = null;
    bool useLocalBin = false;
    bool useSsh = false;
    bool showHelp = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-o" or "--output":
                if (i + 1 < args.Length) outputDir = args[++i];
                break;
            case "--package":
                if (i + 1 < args.Length) packageSpec = args[++i];
                break;
            case "--github":
                if (i + 1 < args.Length) gitSpec = args[++i];
                break;
            case "--project":
                if (i + 1 < args.Length) projectPath = args[++i];
                break;
            case "--ssh":
                useSsh = true;
                break;
            case "--local-bin":
                useLocalBin = true;
                break;
            case "-h" or "--help":
                showHelp = true;
                break;
            default:
                if (!args[i].StartsWith('-'))
                {
                    string arg = args[i];
                    if (Directory.Exists(arg) || File.Exists(arg))
                        projectPath = arg;
                    else
                        unresolvedArg = arg;
                }
                break;
        }
    }

    return new(projectPath, packageSpec, gitSpec, unresolvedArg, outputDir, useLocalBin, useSsh, showHelp);
}

static void PrintUsage()
{
    Console.WriteLine("""
    dotnet install - Install .NET executables to PATH

    Usage:
      dotnet install [project-path] [options]
      dotnet install --github owner/repo[@ref] [options]
      dotnet install --package <name>[@<version>] [options]
      dotnet install list
      dotnet install remove <tool> [<tool>...]

    Arguments:
      project-path        Path to project file or directory (default: current directory)

    Source options:
      --github            Install from a GitHub repository (owner/repo[@ref])
      --package           Install a tool from NuGet
      --ssh               Clone using SSH instead of HTTPS
      --project <path>    Path to .csproj within a git repo

    Install options:
      --local-bin         Install to ~/.local/bin/ instead of ~/.dotnet/bin/
      -o, --output        Installation directory (overrides default and --local-bin)

    Other:
      -h, --help          Show this help

    Examples:
      dotnet install                                   Install current project
      dotnet install src/my-tool                       Install from subdirectory
      dotnet install --github richlander/dotnet-inspect Install from GitHub
      dotnet install --github richlander/dotnet-inspect@v1.0
      dotnet install richlander/dotnet-inspect         Prompts to confirm GitHub
      dotnet install --package dotnet-counters          Install a NuGet tool
      dotnet install list                              List installed tools
      dotnet install remove my-tool                    Remove a tool
    """);
}

record Options(
    string? ProjectPath,
    string? PackageSpec,
    string? GitSpec,
    string? UnresolvedArg,
    string? OutputDir,
    bool UseLocalBin,
    bool UseSsh,
    bool ShowHelp);
