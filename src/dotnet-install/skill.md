---
name: dotnet-install
description: >
  Build, install, list, and remove .NET tools using dotnet-install.
---

# dotnet-install

Install .NET **single-file executables** to PATH — like `cargo install`
and `go install`. Build from source, install from NuGet,
or clone from GitHub.

Only single-file native executables (Native AOT or self-contained
single-file, i.e. CLI tools v2) are supported. For managed or multi-file
tools, use `dotnet tool install` with the .NET SDK.

## Install sources

You point at a source, you get a command on PATH. Every source —
`--package`, `--repo`, `--github`, `--project`, and bare positional
paths — installs to `~/.dotnet/bin` (override with `-o <dir>`). With no
arguments, the current directory is the source (like `dotnet publish`).

A path (or no argument) looks for **a project** and never reads the
repo manifest. `--repo` builds what the repo **advertises**. That is
the whole difference between them.

When several projects match, naming a path opts into the interactive
picker. The bare `dotnet-install` never prompts — it lists the
candidates and exits non-zero, so it is safe in scripts.

```bash
# Whatever is here — a project dir, or a repo root (scans subdirectories)
dotnet-install
dotnet-install .                              # same, but enables the picker
dotnet-install src/my-tool
dotnet-install app.cs                         # file-based app

# Explicit project → global (like dotnet run --project)
dotnet-install --project src/my-tool
dotnet-install --project app.cs               # file-based app

# NuGet package (no SDK required)
dotnet-install --package dotnet-inspect
dotnet-install --package dotnet-inspect@0.16.0  # pinned version

# GitHub repository (must advertise a manifest, or name --project)
dotnet-install --github owner/repo            # tracks default branch, updatable
dotnet-install --github owner/repo --branch main   # tracks branch, updatable
dotnet-install --github owner/repo --tag v2.0      # pinned, no updates
dotnet-install --github owner/repo --rev abc123    # pinned, no updates
dotnet-install --github owner/repo@v2.0            # shorthand, pinned
dotnet-install --github owner/repo --ssh           # clone via SSH

# Any repo — git URL or local path; builds the advertised toolset
dotnet-install --repo https://example.com/repo.git
dotnet-install --repo https://example.com/repo.git --tag v1.0
dotnet-install --repo ../some/local/repo
dotnet-install --repo .                       # this repo's advertised tools
```

Use `-o <dir>` for a custom output location, or `--local-bin` for
`~/.local/bin`.

`--path` is an alias for `--project`. When combined with
`--github` or `--repo`, `--project` specifies a sub-path
within the repository. `--git` is a deprecated alias for `--repo`.

## Repo manifests

A repo advertises its toolset in `.dotnet-install/.dotnet-install.json`.
Each entry in `tools` names exactly one source: a repo-relative
`project` (`.csproj` or `.cs` app), a NuGet `package` (with optional
`version`), or another `repository` (`owner/repo`, with optional
`ref`). They can be mixed, so a manifest can describe a whole
environment rather than just this repo's output.

Every entry is validated before anything installs. Each tool records
its own source, so `update` pulls it from where it came from.

## Git ref options

| Flag         | Pinned | Example                         |
|--------------|--------|---------------------------------|
| (none)       | no     | default branch, tracks upstream |
| `--branch`   | no     | named branch, tracks upstream   |
| `--tag`      | yes    | fixed tag, no updates           |
| `--rev`      | yes    | fixed commit SHA, no updates    |
| `@ref`       | yes    | shorthand in `--github` spec    |

Pinned installs are skipped by `dotnet-install update`.
To change versions, uninstall and reinstall.

## Subcommands

```bash
dotnet-install ls                # list installed tools
dotnet-install rm <tool>         # remove a tool
dotnet-install update [tool]     # update one or all tools
dotnet-install search <query>    # search NuGet
dotnet-install info <tool>       # show tool details
dotnet-install outdated          # check for newer versions
dotnet-install doctor            # diagnose PATH and config
dotnet-install env               # print environment info
dotnet-install completion <sh>   # shell completion setup
```

## Install directory

Tools are installed to `~/.dotnet/bin/` by default.
Override with `DOTNET_TOOL_BIN` env var, `-o <dir>`,
or `--local-bin` (uses `~/.local/bin/`).

## PATH configuration

`dotnet-install` uses a dedicated env file (`~/.dotnet/bin/env`)
that is sourced from the shell's rc file. Run
`dotnet-install doctor --fix` to configure PATH automatically.
To activate in the current shell without restarting:

```bash
. "$HOME/.dotnet/bin/env"          # sh/bash/zsh
source "$HOME/.dotnet/bin/env.fish" # fish
```

## Reliable behavior

- Git updates verify that the remote history is a
  continuation of the local history. If a force push
  is detected, the update is refused — the user must
  uninstall and reinstall the tool.
- Pinned installs (`--tag`, `--rev`, `@ref`) are
  immutable. `update` skips them and reports the
  pinned ref. Changing versions requires an explicit
  uninstall and reinstall.
- Building from source requires the .NET SDK;
  `--package` works without the SDK.
- Only single-file executables install. Managed or
  multi-file tools are refused with a pointer to
  `dotnet tool install`.
- `--require-sourcelink` enforces SourceLink metadata
  in installed assemblies.
