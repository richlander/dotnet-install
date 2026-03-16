---
name: dotnet-install
description: Build, install, list, and remove .NET tools using dotnet-install. Use when the user wants to install a .NET tool from source (local project or GitHub repo), install a NuGet tool package, list installed tools, or remove tools. Triggers on phrases like "install this tool", "install from GitHub", "list installed tools", "remove tool", or references to dotnet-install commands.
argument-hint: [owner/repo | path | --package name | list | remove name]
allowed-tools: Bash, Read, Glob, Grep
---

# dotnet-install

You are helping the user work with `dotnet-install`, a tool that installs .NET executables to PATH — like `cargo install` and `go install`.

## Project structure

The tool lives at `src/dotnet-install/` in this repository:

- `Program.cs` — CLI entry point, argument parsing, subcommand routing
- `Installer.cs` — Core install logic: project evaluation, `dotnet publish`, single/multi-file placement
- `GitSource.cs` — Git clone/fetch from GitHub repos, project discovery, `.dotnet-install.json` manifest
- `ShellHint.cs` — PATH detection and shell-specific setup instructions
- `ListCommand.cs` — Lists installed tools in `~/.dotnet/bin/`
- `RemoveCommand.cs` — Removes installed tools and their supporting files

## Three install modes

### 1. Local project (default)

Builds and installs from a local project directory or the current directory.

```bash
dotnet install                    # current directory
dotnet install src/my-tool        # subdirectory
dotnet install ~/git/my-tool      # explicit path
```

### 2. GitHub repository

Clones (or fetches) a GitHub repo, discovers the project, builds, and installs.

```bash
dotnet install --github richlander/dotnet-inspect
dotnet install --github richlander/dotnet-inspect@v1.0
dotnet install --github richlander/dotnet-inspect --ssh
dotnet install --github richlander/dotnet-inspect --project src/Tool/Tool.csproj
```

If the user types `owner/repo` without `--github`, the tool prompts for confirmation before cloning (anti-typosquatting).

### 3. NuGet package

Downloads and installs a pre-built tool from NuGet.

```bash
dotnet install --package dotnetsay
dotnet install --package dotnet-counters@9.0.0
```

## Subcommands

```bash
dotnet install list              # list installed tools
dotnet install remove <tool>     # remove one or more tools
```

## Key design decisions

- **Install directory**: `~/.dotnet/bin/` (configurable with `-o` or `--local-bin` for `~/.local/bin/`)
- **Git cache**: `~/.nuget/git-tools/<owner>/<repo>/` — persistent clone, `git fetch` on re-install
- **Single-file binaries** (Native AOT): copied directly to install dir
- **Multi-file binaries**: stored in `_<appname>/` subdirectory with a symlink (Unix) or `.cmd` shim (Windows)
- **Project discovery for git repos**: `--project` flag > `.dotnet-install.json` manifest > auto-detect single Exe project (excludes test projects)
- **Source flags**: `--github`, `--package`, and local path are explicit; bare `owner/repo` triggers a confirmation prompt
- **Cross-platform**: Unix uses symlinks and `chmod`; Windows uses `.cmd` shims and `.exe` detection

## When building or running the tool

```bash
# Build
dotnet build src/dotnet-install/dotnet-install.csproj

# Run (via dotnet run)
dotnet run --project src/dotnet-install/dotnet-install.csproj -- <args>

# The project depends on dotnet-inspect (sibling repo at ~/git/dotnet-inspect)
```

## When modifying the tool

- Follow the existing code patterns (manual arg parsing, static classes, top-level statements)
- The project targets `net11.0` with `PublishAot=true` — all code must be AOT-compatible
- Use STJ source generation for any JSON serialization (see `ManifestContext` in `GitSource.cs`)
- Error messages use the format `"  error: <message>"` (two-space indent, lowercase)
- Status messages use `"  <message>"` (two-space indent)
- Test with all three install modes and both `list`/`remove` subcommands
