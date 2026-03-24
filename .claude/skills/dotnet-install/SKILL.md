---
name: dotnet-install
description: >
  Build, install, list, and remove .NET tools
  using dotnet-install.
argument-hint: >
  [owner/repo | path | --package name
  | ls | rm name | search | info | env]
allowed-tools: Bash, Read, Glob, Grep
---

# dotnet-install

You are helping the user work with `dotnet-install`,
a tool that installs .NET executables to PATH
— like `cargo install` and `go install`.

`dotnet-install` relates to `dotnet tool install -g`
the way yarn relates to npm: it uses the same package
registry (NuGet) but provides a different installation
model. Where `dotnet tool install -g` places shim
scripts in `~/.dotnet/tools/` backed by deeply nested
binaries in `.store/`, `dotnet-install` places real
binaries directly in `~/.dotnet/bin/` — a flat,
transparent layout like Go's `~/go/bin/` or Cargo's
`~/.cargo/bin/`. Users can acquire dotnet-install
itself via `dotnet tool install -g` as a bootstrap,
after which `dotnet-install setup` graduates the tool
to `~/.dotnet/bin/` and sheds the dotnet tool scaffolding.

## Two directories — don't confuse them

| Directory            | Owner                    | Contents                   |
| -------------------- | ------------------------ | -------------------------- |
| `~/.dotnet/tools/`   | `dotnet tool install -g` | Shim scripts → `.store/`   |
| `~/.dotnet/bin/`     | `dotnet-install`         | Real binaries, flat layout |

`dotnet-install` itself lives at `~/.dotnet/bin/dotnet-install`.
Override with `DOTNET_TOOL_BIN` env var, `-o`, or
`--local-bin` (`~/.local/bin/`).

## Invoking the tool

```bash
dotnet-install <args>
# or via dotnet prefix matching:
dotnet install <args>
```

When working in this repo (development):

```bash
dotnet run --project src/dotnet-install -- <args>
```

## Project structure

The tool lives at `src/dotnet-install/` in this repo:

- `Program.cs` — CLI entry point
- `CommandLineBuilder.cs` — System.CommandLine command/option
  definitions and handler wiring
- `Installer.cs` — Core install logic: project eval,
  `dotnet publish`, single/multi-file placement,
  NuGet package install, file-based app support
- `GitSource.cs` — Git clone/fetch from GitHub repos,
  project discovery, `.dotnet-install.json` manifest
- `ShellHint.cs` — PATH detection, shell-specific
  setup instructions, `DOTNET_TOOL_BIN` env var
- `SetupCommand.cs` — Shell PATH config, self-install
  from NuGet (bootstrap graduation), shed dotnet tool
- `EnvCommand.cs` — Print environment info (`cargo env` style)
- `ProjectSelector.cs` — Interactive arrow-key selector
  for repos with multiple executable projects
- `HostDispatch.cs` — Busybox-style dispatch for managed
  tools (Unix symlink-based, Windows .cmd shim-based)
- `ListCommand.cs` — Lists installed tools
- `RemoveCommand.cs` — Removes installed tools
- `UpdateCommand.cs` — Updates installed tools
- `SearchCommand.cs` — Search NuGet for packages
- `InfoCommand.cs` — Show tool details and provenance
- `OutdatedCommand.cs` — Check for newer versions
- `RunCommand.cs` — Run without installing (npx-like)
- `CompletionCommand.cs` — Shell completion setup
- `SkillCommand.cs` — Prints embedded skill definition
- `skill.md` — Embedded skill for AI assistants (end-user)
- `HelpWriter.cs` — Markout-based help formatting

## Three install modes

### 1. Local project (default)

Builds and installs from a local project directory or the current directory.
Supports both `.csproj` projects and file-based apps (`.cs` with `#:property` directives).

```bash
dotnet install                    # current directory
dotnet install src/my-tool        # subdirectory
dotnet install ~/git/my-tool      # explicit path
dotnet install app.cs             # file-based app
```

### 2. GitHub repository

Clones (or fetches) a GitHub repo, discovers the project, builds, and installs.

```bash
dotnet install --github richlander/dotnet-inspect
dotnet install --github richlander/dotnet-inspect@v1.0
dotnet install --github richlander/dotnet-inspect --ssh
dotnet install --github richlander/dotnet-inspect --project src/Tool/Tool.csproj
```

If the user types `owner/repo` without `--github`,
the tool prompts for confirmation before cloning
(anti-typosquatting).

### 3. NuGet package

Downloads and installs a pre-built tool from NuGet.

```bash
dotnet install --package dotnetsay
dotnet install --package dotnet-counters@9.0.0
```

### Multiple tools at once

Positional args can mix sources. When multiple args
are given, confirmation prompts are skipped.

```bash
dotnet install dotnetsay dotnet-counters    # two NuGet packages
dotnet install richlander/dotnetsay app.cs  # GitHub + local file-based app
```

## Subcommands

```bash
dotnet install ls                # list installed tools
dotnet install rm <tool>         # remove one or more tools
dotnet install update <tool>     # update installed tools
dotnet install search <query>    # search NuGet
dotnet install info <tool>       # show tool details
dotnet install outdated          # check for newer versions
dotnet install run <pkg> [args]  # run without installing (npx-like)
dotnet install setup             # configure PATH + DOTNET_TOOL_BIN
dotnet install env               # print environment info
dotnet install completion        # shell completion setup
```

## Behavior

- **Bare vs explicit**: bare positional args prompt to
  confirm remote sources (NuGet/GitHub); explicit flags
  (`--package`, `--github`) skip all prompts
- **Roll-forward**: remote installs (NuGet) auto-enable
  roll-forward; local installs prompt the user
  (`--allow-roll-forward` suppresses)
- **SDK preflight**: building from source checks for the
  .NET SDK before `dotnet publish` and suggests
  `--package` as the SDK-free alternative

## When building or running the tool

```bash
# Build
dotnet build src/dotnet-install/dotnet-install.csproj

# Run (via dotnet run)
dotnet run --project src/dotnet-install/dotnet-install.csproj -- <args>

# Tests
dotnet run --project test/dotnet-install.Tests
```

## When modifying the tool

- CLI is built with System.CommandLine — commands and
  options defined in `CommandLineBuilder.cs`
- Help output uses Markout serialization (`HelpWriter.cs`)
- The project targets `net11.0` with
  `PublishAot=true` — all code must be AOT-compatible
- Use STJ source generation for any JSON
  serialization (see `ManifestContext` in
  `GitSource.cs`)
- Error messages: `"error: <message>"` to stderr
- Status messages to stdout
- Single-file (AOT) binaries go directly in install dir;
  multi-file (managed) go in `_<appname>/` with a
  symlink (Unix) or `.cmd` shim (Windows)
- Git repos cached at `~/.nuget/git-tools/<owner>/<repo>/`
- Project discovery order: `--project` > manifest >
  auto-detect Exe > file-based apps (≤12 → selector)
- See `DESIGN.md` for full architecture rationale
- Test with all three install modes and subcommands
