/// <summary>
/// Generate shell completion scripts for bash, zsh, fish, and PowerShell.
/// Uses System.CommandLine's [suggest] directive under the hood.
/// </summary>
static class CompletionCommand
{
    public static int Run(string shell)
    {
        string script = shell.ToLowerInvariant() switch
        {
            "bash" => BashScript(),
            "zsh" => ZshScript(),
            "fish" => FishScript(),
            "powershell" or "pwsh" => PowerShellScript(),
            _ => ""
        };

        if (string.IsNullOrEmpty(script))
        {
            Console.Error.WriteLine($"error: unsupported shell '{shell}'");
            Console.Error.WriteLine("Supported: bash, zsh, fish, powershell");
            return 1;
        }

        Console.Write(script);
        return 0;
    }

    static string BashScript() => """
        # bash completion for dotnet-install
        # Add to ~/.bashrc: eval "$(dotnet-install completion bash)"
        _dotnet_install_completions() {
            local cur="${COMP_WORDS[COMP_CWORD]}"
            local words="${COMP_WORDS[*]}"
            local position=$COMP_POINT

            COMPREPLY=()
            local suggestions
            suggestions=$(dotnet-install "[suggest:$position]" $words 2>/dev/null)
            if [ $? -eq 0 ]; then
                COMPREPLY=( $(compgen -W "$suggestions" -- "$cur") )
            fi
        }
        complete -F _dotnet_install_completions dotnet-install
        """;

    static string ZshScript() => """
        # zsh completion for dotnet-install
        # Add to ~/.zshrc: eval "$(dotnet-install completion zsh)"
        _dotnet_install() {
            local -a completions
            local words="${words[*]}"
            local position=$CURSOR

            completions=(${(f)"$(dotnet-install "[suggest:$position]" $words 2>/dev/null)"})
            compadd -a completions
        }
        compdef _dotnet_install dotnet-install
        """;

    static string FishScript() => """
        # fish completion for dotnet-install
        # Add to fish config: dotnet-install completion fish | source
        complete -c dotnet-install -f -a '(
            set -l words (commandline -cop)
            set -l position (commandline -C)
            dotnet-install "[suggest:$position]" $words 2>/dev/null
        )'
        """;

    static string PowerShellScript() => """
        # PowerShell completion for dotnet-install
        # Add to $PROFILE: dotnet-install completion powershell | Invoke-Expression
        Register-ArgumentCompleter -Native -CommandName dotnet-install -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            $args = $commandAst.ToString().Split()
            $suggestions = & dotnet-install "[suggest:$cursorPosition]" @args 2>$null
            $suggestions | ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        }
        """;
}
