using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

/// <summary>
/// Builds the System.CommandLine command structure for dotnet-install.
/// </summary>
static class CommandLineBuilder
{
    /// <summary>
    /// The root command name. System.CommandLine strips a leading token whose file
    /// name matches this (treating it as the invocation path), which would swallow a
    /// legitimate positional path ending in it.
    /// </summary>
    internal const string CommandName = "dotnet-install";

    /// <summary>
    /// Guards the first argument against System.CommandLine's invocation-path
    /// stripping: when it is a positional path whose file name matches
    /// <see cref="CommandName"/> (e.g. <c>./dotnet-install</c> or a clone directory
    /// of this repo), it would otherwise be silently dropped as the invocation path.
    /// The stripping only affects the very first token, so when there are other
    /// arguments the offending path is moved to the end (past any options, which
    /// bind by name regardless of position); when it is the only argument, it is
    /// separated with <c>--</c>. Returns the args unchanged otherwise.
    /// </summary>
    internal static string[] NormalizeArgs(string[] args)
    {
        if (args.Length > 0 && !args[0].StartsWith('-') &&
            string.Equals(Path.GetFileNameWithoutExtension(args[0].TrimEnd('/', '\\')),
                CommandName, StringComparison.OrdinalIgnoreCase))
        {
            // Sole token: separate it so it is parsed as the project operand rather
            // than the invocation path.
            if (args.Length == 1)
                return ["--", args[0]];

            // Otherwise move it past the remaining arguments so trailing options
            // still bind, while it is no longer the (stripped) first token.
            return [.. args[1..], args[0]];
        }

        return args;
    }

    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Install .NET executables to PATH — like cargo install and go install");

        // --- Shared options ---

        var outputOption = new Option<string?>("-o", "--output")
        {
            Description = "Installation directory (overrides default)",
            HelpName = "dir"
        };
        var localBinOption = new Option<bool>("--local-bin")
        {
            Description = "Install to ~/.local/bin/ instead of ~/.dotnet/bin/"
        };
        var sshOption = new Option<bool>("--ssh")
        {
            Description = "Clone using SSH instead of HTTPS"
        };
        var sourceLinkOption = new Option<bool>("--require-sourcelink")
        {
            Description = "Require SourceLink metadata in installed assemblies"
        };

        // --- Install sources (default command arguments/options) ---

        var projectArg = new Argument<string?>("project")
        {
            Description = "Path to project to install",
            Arity = ArgumentArity.ZeroOrOne
        };
        var packageOption = new Option<string?>("--package")
        {
            Description = "Install a tool from NuGet",
            HelpName = "name[@version]"
        };
        var githubOption = new Option<string?>("--github")
        {
            Description = "Install from a GitHub repository",
            HelpName = "owner/repo[@ref]"
        };
        var repoOption = new Option<string?>("--repo")
        {
            Description = "Build a repo (git URL or local path) and install its advertised tools",
            HelpName = "url|path"
        };
        repoOption.Aliases.Add("--git");
        var branchOption = new Option<string?>("--branch")
        {
            Description = "Git branch to track (updatable)",
            HelpName = "name"
        };
        var tagOption = new Option<string?>("--tag")
        {
            Description = "Git tag to install (pinned)",
            HelpName = "name"
        };
        var revOption = new Option<string?>("--rev")
        {
            Description = "Git commit SHA to install (pinned)",
            HelpName = "sha"
        };
        var projectOption = new Option<string?>("--project")
        {
            Description = "Path to project (or sub-path within a git repo)",
            HelpName = "path"
        };
        projectOption.Aliases.Add("--path");

        rootCommand.Arguments.Add(projectArg);
        rootCommand.Options.Add(packageOption);
        rootCommand.Options.Add(githubOption);
        rootCommand.Options.Add(repoOption);
        rootCommand.Options.Add(branchOption);
        rootCommand.Options.Add(tagOption);
        rootCommand.Options.Add(revOption);
        rootCommand.Options.Add(projectOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(localBinOption);
        rootCommand.Options.Add(sshOption);
        rootCommand.Options.Add(sourceLinkOption);

        // --- Subcommands ---

        var doctorFixOption = new Option<bool>("--fix")
        {
            Description = "Attempt to repair issues found"
        };
        doctorFixOption.Aliases.Add("--repair");
        var doctorPathOption = new Option<bool>("--path")
        {
            Description = "Only check/fix shell PATH configuration"
        };
        var doctorCommand = new Command("doctor", "Check environment setup");
        doctorCommand.Options.Add(doctorFixOption);
        doctorCommand.Options.Add(doctorPathOption);
        doctorCommand.SetAction(async (parseResult, ct) =>
        {
            bool fix = parseResult.GetValue(doctorFixOption);
            bool pathOnly = parseResult.GetValue(doctorPathOption);
            return await DoctorCommand.Run(Installer.DefaultInstallDir, fix, pathOnly);
        });

        var configKeyArg = new Argument<string?>("key")
        {
            Description = "Config key to get or set",
            Arity = ArgumentArity.ZeroOrOne
        };
        var configValueArg = new Argument<string?>("value")
        {
            Description = "Value to set",
            Arity = ArgumentArity.ZeroOrOne
        };
        var configCommand = new Command("config", "View and update settings");
        configCommand.Arguments.Add(configKeyArg);
        configCommand.Arguments.Add(configValueArg);
        configCommand.SetAction((parseResult, ct) =>
        {
            return Task.FromResult(ConfigCommand.Run(
                Installer.DefaultInstallDir,
                parseResult.GetValue(configKeyArg),
                parseResult.GetValue(configValueArg)));
        });

        var listNoHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers" };
        listNoHeaderOption.Aliases.Add("--nh");
        var listColumnsOption = new Option<string?>("--columns") { Description = "Select columns (comma-separated)" };
        listColumnsOption.Aliases.Add("-S");
        var listJsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var listCommand = new Command("ls", "List installed tools");
        listCommand.Aliases.Add("list");
        listCommand.Options.Add(listNoHeaderOption);
        listCommand.Options.Add(listColumnsOption);
        listCommand.Options.Add(listJsonOption);
        listCommand.SetAction((parseResult, ct) =>
        {
            bool noHeader = parseResult.GetValue(listNoHeaderOption);
            string? columns = parseResult.GetValue(listColumnsOption);
            bool json = parseResult.GetValue(listJsonOption);
            ListCommand.Run(Installer.DefaultInstallDir, noHeader, columns, json);
            return Task.FromResult(0);
        });

        var updateToolsArg = new Argument<string[]>("tool")
        {
            Description = "Tools to update (all if omitted)",
            Arity = ArgumentArity.ZeroOrMore
        };
        var updateCommand = new Command("update", "Check for updates and reinstall");
        updateCommand.Arguments.Add(updateToolsArg);
        updateCommand.SetAction(async (parseResult, ct) =>
        {
            string[] tools = parseResult.GetValue(updateToolsArg) ?? [];
            return await UpdateCommand.RunAsync(Installer.DefaultInstallDir, tools);
        });

        var removeToolsArg = new Argument<string[]>("tool")
        {
            Description = "Tools to remove",
            Arity = ArgumentArity.OneOrMore
        };
        var removeCommand = new Command("rm", "Remove installed tools");
        removeCommand.Aliases.Add("remove");
        removeCommand.Arguments.Add(removeToolsArg);
        removeCommand.SetAction((parseResult, ct) =>
        {
            string[] tools = parseResult.GetValue(removeToolsArg) ?? [];
            return Task.FromResult(RemoveCommand.Run(Installer.DefaultInstallDir, tools));
        });

        // Hidden "install" alias — install is the default action on the root command,
        // but typing "dotnet-install install ..." is natural after using remove/update.
        var installCommand = new Command("install", "Install a .NET tool") { Hidden = true };
        installCommand.Arguments.Add(new Argument<string?>("project") { Arity = ArgumentArity.ZeroOrOne });
        installCommand.Options.Add(packageOption);
        installCommand.Options.Add(githubOption);
        installCommand.Options.Add(repoOption);
        installCommand.Options.Add(branchOption);
        installCommand.Options.Add(tagOption);
        installCommand.Options.Add(revOption);
        installCommand.Options.Add(projectOption);
        installCommand.Options.Add(outputOption);
        installCommand.Options.Add(localBinOption);
        installCommand.Options.Add(sshOption);
        installCommand.Options.Add(sourceLinkOption);
        installCommand.SetAction(async (parseResult, ct) =>
        {
            var arg = parseResult.GetValue<string?>("project");
            return await InstallAction.RunAsync(
                arg,
                parseResult.GetValue(packageOption),
                parseResult.GetValue(githubOption),
                parseResult.GetValue(repoOption),
                parseResult.GetValue(branchOption),
                parseResult.GetValue(tagOption),
                parseResult.GetValue(revOption),
                parseResult.GetValue(projectOption),
                parseResult.GetValue(outputOption),
                parseResult.GetValue(localBinOption),
                parseResult.GetValue(sshOption),
                parseResult.GetValue(sourceLinkOption));
        });

        // --- search command ---
        var searchQueryArg = new Argument<string>("query") { Description = "NuGet search query" };
        var searchTakeOption = new Option<int>("--take") { Description = "Max results to return", DefaultValueFactory = _ => 20 };
        var searchNoHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers" };
        searchNoHeaderOption.Aliases.Add("--nh");
        var searchColumnsOption = new Option<string?>("--columns") { Description = "Select columns (comma-separated)" };
        searchColumnsOption.Aliases.Add("-S");
        var searchJsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var searchCommand = new Command("search", "Search NuGet for tool packages");
        searchCommand.Arguments.Add(searchQueryArg);
        searchCommand.Options.Add(searchTakeOption);
        searchCommand.Options.Add(searchNoHeaderOption);
        searchCommand.Options.Add(searchColumnsOption);
        searchCommand.Options.Add(searchJsonOption);
        searchCommand.SetAction(async (parseResult, ct) =>
        {
            return await SearchCommand.RunAsync(
                parseResult.GetValue(searchQueryArg)!,
                parseResult.GetValue(searchTakeOption),
                parseResult.GetValue(searchNoHeaderOption),
                parseResult.GetValue(searchColumnsOption),
                parseResult.GetValue(searchJsonOption));
        });

        // --- info command ---
        var infoToolArg = new Argument<string>("tool") { Description = "Name of installed tool" };
        var infoJsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var infoCommand = new Command("info", "Show detailed information about an installed tool");
        infoCommand.Arguments.Add(infoToolArg);
        infoCommand.Options.Add(infoJsonOption);
        infoCommand.SetAction((parseResult, ct) =>
        {
            return Task.FromResult(InfoCommand.Run(
                Installer.DefaultInstallDir,
                parseResult.GetValue(infoToolArg)!,
                parseResult.GetValue(infoJsonOption)));
        });

        // --- outdated command ---
        var outdatedNoHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers" };
        outdatedNoHeaderOption.Aliases.Add("--nh");
        var outdatedColumnsOption = new Option<string?>("--columns") { Description = "Select columns (comma-separated)" };
        outdatedColumnsOption.Aliases.Add("-S");
        var outdatedJsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var outdatedCommand = new Command("outdated", "Check for available updates without installing");
        outdatedCommand.Options.Add(outdatedNoHeaderOption);
        outdatedCommand.Options.Add(outdatedColumnsOption);
        outdatedCommand.Options.Add(outdatedJsonOption);
        outdatedCommand.SetAction(async (parseResult, ct) =>
        {
            return await OutdatedCommand.RunAsync(
                Installer.DefaultInstallDir,
                parseResult.GetValue(outdatedNoHeaderOption),
                parseResult.GetValue(outdatedColumnsOption),
                parseResult.GetValue(outdatedJsonOption));
        });

        // --- completion command ---
        var completionShellArg = new Argument<string>("shell") { Description = "Shell type (bash, zsh, fish, powershell)" };
        var completionCommand = new Command("completion", "Generate shell completion script");
        completionCommand.Arguments.Add(completionShellArg);
        completionCommand.SetAction((parseResult, ct) =>
        {
            return Task.FromResult(CompletionCommand.Run(parseResult.GetValue(completionShellArg)!));
        });

        var envCommand = new Command("env", "Print environment information");
        envCommand.SetAction((parseResult, ct) =>
        {
            EnvCommand.Run(Installer.DefaultInstallDir);
            return Task.FromResult(0);
        });

        var skillCommand = new Command("skill", "Print the AI skill definition for this tool");
        skillCommand.SetAction((parseResult, ct) =>
        {
            return Task.FromResult(SkillCommand.Run());
        });

        rootCommand.Subcommands.Add(doctorCommand);
        rootCommand.Subcommands.Add(configCommand);
        rootCommand.Subcommands.Add(listCommand);
        rootCommand.Subcommands.Add(updateCommand);
        rootCommand.Subcommands.Add(removeCommand);
        rootCommand.Subcommands.Add(installCommand);
        rootCommand.Subcommands.Add(searchCommand);
        rootCommand.Subcommands.Add(infoCommand);
        rootCommand.Subcommands.Add(outdatedCommand);
        rootCommand.Subcommands.Add(completionCommand);
        rootCommand.Subcommands.Add(envCommand);
        rootCommand.Subcommands.Add(skillCommand);

        // --- Default install action ---

        rootCommand.SetAction(async (parseResult, ct) =>
        {
            string? project = parseResult.GetValue(projectArg);
            string? package = parseResult.GetValue(packageOption);
            string? github = parseResult.GetValue(githubOption);
            string? git = parseResult.GetValue(repoOption);
            string? branch = parseResult.GetValue(branchOption);
            string? tag = parseResult.GetValue(tagOption);
            string? rev = parseResult.GetValue(revOption);
            string? projectPath = parseResult.GetValue(projectOption);
            string? outputDir = parseResult.GetValue(outputOption);
            bool useLocalBin = parseResult.GetValue(localBinOption);
            bool useSsh = parseResult.GetValue(sshOption);
            bool requireSourceLink = parseResult.GetValue(sourceLinkOption);

            return await InstallAction.RunAsync(
                project, package, github, git, branch, tag, rev, projectPath,
                outputDir, useLocalBin, useSsh, requireSourceLink);
        });

        // --- Custom help ---

        var helpOption = rootCommand.Options.OfType<HelpOption>().FirstOrDefault();
        if (helpOption != null)
            helpOption.Action = new HelpOptionAction();

        // Apply to subcommands too
        foreach (var sub in rootCommand.Subcommands)
        {
            var subHelp = sub.Options.OfType<HelpOption>().FirstOrDefault();
            if (subHelp != null)
                subHelp.Action = new HelpOptionAction();
        }

        return rootCommand;
    }
}
