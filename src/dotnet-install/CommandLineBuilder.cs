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

        var projectArg = new Argument<string?>("project-path")
        {
            Description = "Path to project file or directory",
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
        var projectOption = new Option<string?>("--project")
        {
            Description = "Path to .csproj within a git repo",
            HelpName = "path"
        };

        rootCommand.Arguments.Add(projectArg);
        rootCommand.Options.Add(packageOption);
        rootCommand.Options.Add(githubOption);
        rootCommand.Options.Add(projectOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(localBinOption);
        rootCommand.Options.Add(sshOption);
        rootCommand.Options.Add(rollForwardOption);
        rootCommand.Options.Add(sourceLinkOption);

        // --- Subcommands ---

        var setupCommand = new Command("setup", "Configure shell PATH and create self-link");
        setupCommand.SetAction((parseResult, ct) =>
        {
            SetupCommand.Run(Installer.DefaultInstallDir);
            return Task.FromResult(0);
        });

        var listCommand = new Command("list", "List installed tools");
        listCommand.SetAction((parseResult, ct) =>
        {
            ListCommand.Run(Installer.DefaultInstallDir);
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
        var removeCommand = new Command("remove", "Remove installed tools");
        removeCommand.Arguments.Add(removeToolsArg);
        removeCommand.SetAction((parseResult, ct) =>
        {
            string[] tools = parseResult.GetValue(removeToolsArg) ?? [];
            return Task.FromResult(RemoveCommand.Run(Installer.DefaultInstallDir, tools));
        });

        rootCommand.Subcommands.Add(setupCommand);
        rootCommand.Subcommands.Add(listCommand);
        rootCommand.Subcommands.Add(updateCommand);
        rootCommand.Subcommands.Add(removeCommand);

        // --- Default install action ---

        rootCommand.SetAction(async (parseResult, ct) =>
        {
            string? project = parseResult.GetValue(projectArg);
            string? package = parseResult.GetValue(packageOption);
            string? github = parseResult.GetValue(githubOption);
            string? projectPath = parseResult.GetValue(projectOption);
            string? outputDir = parseResult.GetValue(outputOption);
            bool useLocalBin = parseResult.GetValue(localBinOption);
            bool useSsh = parseResult.GetValue(sshOption);
            bool allowRollForward = parseResult.GetValue(rollForwardOption);
            bool requireSourceLink = parseResult.GetValue(sourceLinkOption);

            return await InstallAction.RunAsync(
                project, package, github, projectPath,
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
