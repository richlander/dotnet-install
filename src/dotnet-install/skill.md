---
name: dotnet-install
description: >
  Install .NET executables to PATH — like cargo install
  and go install. Build from source, install from NuGet,
  or clone from GitHub.
---

# dotnet-install

Install .NET executables to PATH — like `cargo install`
and `go install`.

## Install modes

```bash
dotnet-install .                              # build & install current project
dotnet-install src/my-tool                    # subdirectory
dotnet-install app.cs                         # file-based app
dotnet-install --package dotnetsay            # install from NuGet
dotnet-install --package dotnet-counters@9.0  # pinned version
dotnet-install --github richlander/my-tool    # install from GitHub
dotnet-install --github owner/repo@v1.0      # tagged release
```

Bare positional args are auto-classified: local paths
build from source, `owner/repo` patterns prompt for
GitHub, and single names prompt for NuGet. Use explicit
flags (`--package`, `--github`) to skip prompts.

## Subcommands

```bash
dotnet-install ls                # list installed tools
dotnet-install rm <tool>         # remove a tool
dotnet-install update [tool]     # update one or all tools
dotnet-install search <query>    # search NuGet
dotnet-install info <tool>       # show tool details
dotnet-install outdated          # check for newer versions
dotnet-install run <pkg> [args]  # run without installing
dotnet-install doctor            # diagnose PATH and config
dotnet-install env               # print environment info
dotnet-install completion <sh>   # shell completion setup
```

## Install directory

Tools are installed to `~/.dotnet/bin/` by default.
Override with `DOTNET_TOOL_BIN` env var, `-o <dir>`,
or `--local-bin` (uses `~/.local/bin/`).

## Behavior

- Remote installs (NuGet) auto-enable roll-forward
  if the tool targets an older runtime
- Building from source requires the .NET SDK;
  `--package` works without the SDK
- Multiple positional args skip confirmation prompts
