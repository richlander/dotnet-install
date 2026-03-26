using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

/// <summary>
/// Builds the System.CommandLine command structure for dotnet-install.
/// </summary>
static class CommandLineBuilder
{
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
        var rollForwardOption = new Option<bool>("--allow-roll-forward")
        {
            Description = "Allow tool to run on a newer .NET runtime version"
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
        var gitOption = new Option<string?>("--git")
        {
            Description = "Install from a git URL",
            HelpName = "url"
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
        rootCommand.Options.Add(gitOption);
        rootCommand.Options.Add(projectOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(localBinOption);
        rootCommand.Options.Add(sshOption);
        rootCommand.Options.Add(rollForwardOption);
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
        installCommand.Options.Add(gitOption);
        installCommand.Options.Add(projectOption);
        installCommand.Options.Add(outputOption);
        installCommand.Options.Add(localBinOption);
        installCommand.Options.Add(sshOption);
        installCommand.Options.Add(rollForwardOption);
        installCommand.Options.Add(sourceLinkOption);
        installCommand.SetAction(async (parseResult, ct) =>
        {
            var arg = parseResult.GetValue<string?>("project");
            return await InstallAction.RunAsync(
                arg,
                parseResult.GetValue(packageOption),
                parseResult.GetValue(githubOption),
                parseResult.GetValue(gitOption),
                parseResult.GetValue(projectOption),
                parseResult.GetValue(outputOption),
                parseResult.GetValue(localBinOption),
                parseResult.GetValue(sshOption),
                parseResult.GetValue(rollForwardOption),
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

        // --- run command ---
        var runPackageArg = new Argument<string>("package") { Description = "NuGet package name[@version]" };
        var runToolArgsArg = new Argument<string[]>("args")
        {
            Description = "Arguments to pass to the tool",
            Arity = ArgumentArity.ZeroOrMore
        };
        var runRollForwardOption = new Option<bool>("--allow-roll-forward")
        {
            Description = "Allow tool to run on a newer .NET runtime version"
        };
        var runCommand = new Command("run", "Run a NuGet tool without installing it (like npx)");
        runCommand.Arguments.Add(runPackageArg);
        runCommand.Arguments.Add(runToolArgsArg);
        runCommand.Options.Add(runRollForwardOption);
        runCommand.SetAction(async (parseResult, ct) =>
        {
            return await RunCommand.RunAsync(
                parseResult.GetValue(runPackageArg)!,
                parseResult.GetValue(runToolArgsArg) ?? [],
                parseResult.GetValue(runRollForwardOption));
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
        rootCommand.Subcommands.Add(runCommand);
        rootCommand.Subcommands.Add(completionCommand);
        rootCommand.Subcommands.Add(envCommand);
        rootCommand.Subcommands.Add(skillCommand);

        // --- Default install action ---

        rootCommand.SetAction(async (parseResult, ct) =>
        {
            string? project = parseResult.GetValue(projectArg);
            string? package = parseResult.GetValue(packageOption);
            string? github = parseResult.GetValue(githubOption);
            string? git = parseResult.GetValue(gitOption);
            string? projectPath = parseResult.GetValue(projectOption);
            string? outputDir = parseResult.GetValue(outputOption);
            bool useLocalBin = parseResult.GetValue(localBinOption);
            bool useSsh = parseResult.GetValue(sshOption);
            bool allowRollForward = parseResult.GetValue(rollForwardOption);
            bool requireSourceLink = parseResult.GetValue(sourceLinkOption);

            return await InstallAction.RunAsync(
                project, package, github, git, projectPath,
                outputDir, useLocalBin, useSsh, allowRollForward, requireSourceLink);
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
