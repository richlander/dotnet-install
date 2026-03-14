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
    result = await Installer.InstallPackageAsync(options.PackageSpec, installDir);
}
else
{
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
    bool useLocalBin = false;
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
            case "--local-bin":
                useLocalBin = true;
                break;
            case "-h" or "--help":
                showHelp = true;
                break;
            default:
                if (!args[i].StartsWith('-'))
                    projectPath = args[i];
                break;
        }
    }

    return new(projectPath, packageSpec, outputDir, useLocalBin, showHelp);
}

static void PrintUsage()
{
    Console.WriteLine("""
    dotnet install - Install .NET executables to PATH

    Usage:
      dotnet install [project-path] [options]
      dotnet install --package <name>[@<version>] [options]

    Arguments:
      project-path    Path to project file or directory (default: current directory)

    Options:
      --package       Install a tool from NuGet instead of building from source
      --local-bin     Install to ~/.local/bin/ instead of ~/.dotnet/bin/
      -o, --output    Installation directory (overrides default and --local-bin)
      -h, --help      Show this help

    Examples:
      dotnet install                              Install current project
      dotnet install src/my-tool                  Install from subdirectory
      dotnet install --package dotnet-counters    Install a NuGet tool
      dotnet install --local-bin                  Install to ~/.local/bin/
      dotnet install -o ~/tools                   Install to custom location
    """);
}

record Options(string? ProjectPath, string? PackageSpec, string? OutputDir, bool UseLocalBin, bool ShowHelp);
