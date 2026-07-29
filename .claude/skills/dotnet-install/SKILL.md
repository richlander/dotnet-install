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
itself via `dotnet tool install -g`; it stays a managed
.NET tool and runs `dotnet-install doctor --fix` to add
`~/.dotnet/bin/` to PATH.

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
  `dotnet publish`, single-file placement,
  NuGet package install, file-based app support
- `GitSource.cs` — Git clone/fetch from GitHub repos,
  project discovery, `.dotnet-install.json` manifest
- `ShellHint.cs` — PATH detection, shell-specific
  setup instructions, `DOTNET_TOOL_BIN` env var
- `DoctorCommand.cs` — Shell PATH config and
  environment checks (`doctor`)
- `EnvCommand.cs` — Print environment info (`cargo env` style)
- `ProjectSelector.cs` — Interactive arrow-key selector
  for repos with multiple executable projects
- `ListCommand.cs` — Lists installed tools
- `RemoveCommand.cs` — Removes installed tools
- `UpdateCommand.cs` — Updates installed tools
- `SearchCommand.cs` — Search NuGet for packages
- `InfoCommand.cs` — Show tool details and provenance
- `OutdatedCommand.cs` — Check for newer versions
- `CompletionCommand.cs` — Shell completion setup
- `SkillCommand.cs` — Prints embedded skill definition
- `skill.md` — Embedded skill for AI assistants (end-user)
- `HelpWriter.cs` — Markout-based help formatting

## Install target

Every source installs to `~/.dotnet/bin`: `--package`,
`--repo`, `--github`, `--project`, and bare positional paths.
This matches `cargo install` and `go install` — you point at a
thing, you get a tool on PATH. There is a single install
location; `-o <dir>` overrides it, and `--local-bin` selects
`~/.local/bin`. Nothing about a repo's contents changes where
a tool lands.

`dotnet install` with no args treats the current directory as
the source, like `dotnet publish`. `--help` prints help.

## Path vs. repo

The two source-build gestures differ only in what they read:

- `dotnet install <path>`, or no argument (meaning the current
  directory), looks for **a project**. It never reads
  `.dotnet-install/.dotnet-install.json`.
- `dotnet install --repo <path>` builds what the repo
  **advertises** in that manifest — one tool or a toolset.

## Install modes

### 1. Local project

Builds and installs from a local project directory.
Supports both `.csproj` projects and file-based apps (`.cs` with `#:property` directives).
The tree is scanned for executable projects. If several are found,
the interactive selector is shown **only when a path was named** —
the bare command lists candidates and exits non-zero instead, so it
never prompts unasked (important for scripts and agent loops).

```bash
dotnet install                    # current dir, never prompts
dotnet install .                  # current dir, enables the picker
dotnet install src/my-tool        # subdirectory
dotnet install ~/git/my-tool      # explicit path
dotnet install app.cs             # file-based app
```

### 2. Repository (`--repo` / `--github`)

Clones (or fetches) a repo, discovers the advertised project,
builds, and installs globally.
`--repo` takes a git URL (clone) or a local repo path (build in place);
`--github owner/repo` is shorthand for a `--repo` GitHub URL.
`--git` is a deprecated hidden alias for `--repo`.

```bash
dotnet install --repo https://example.com/some/repo.git
dotnet install --repo ../some/local/repo
dotnet install --github richlander/dotnet-runtimeinfo
dotnet install --github richlander/dotnet-runtimeinfo@v3.0.1
dotnet install --github richlander/dotnet-runtimeinfo --ssh
dotnet install --github richlander/dotnet-runtimeinfo --project dotnet-runtimeinfo.csproj
```

`--repo`/`--github` require the repo to advertise a `tools`
array in `.dotnet-install/.dotnet-install.json`, unless
`--project` names one explicitly. If the user types
`owner/repo` without `--github`, the tool prompts for
confirmation before cloning (anti-typosquatting).

### 3. NuGet package

Downloads and installs a pre-built tool from NuGet.

```bash
dotnet install --package dotnet-inspect
dotnet install --package dotnet-inspect@0.16.0
```

### Repo manifests (`--repo`)

A repo advertises its toolset in
`.dotnet-install/.dotnet-install.json`. Each entry in `tools`
names exactly one source:

| Source | Means | Optional |
| --- | --- | --- |
| `project` | repo-relative `.csproj` or `.cs` app | |
| `package` | NuGet package id | `version` |
| `repository` | `owner/repo` on GitHub | `ref` |

All three can be mixed, so a manifest can describe a whole
environment, not just what the repo builds. Each installed
tool records its own source, so `update` pulls it from where
it actually came from.

### Multiple tools at once

Positional args can mix sources. When multiple args
are given, confirmation prompts are skipped.

```bash
dotnet install dotnet-inspect dotnet-runtimeinfo    # two NuGet packages
dotnet install richlander/dotnet-runtimeinfo app.cs # GitHub + local file-based app
```

## Subcommands

```bash
dotnet install ls                # list installed tools
dotnet install rm <tool>         # remove one or more tools
dotnet install update <tool>     # update installed tools
dotnet install search <query>    # search NuGet
dotnet install info <tool>       # show tool details
dotnet install outdated          # check for newer versions
dotnet install doctor            # configure PATH + DOTNET_TOOL_BIN
dotnet install env               # print environment info
dotnet install completion        # shell completion setup
```

## Behavior

- **Bare vs explicit**: bare positional args prompt to
  confirm remote sources (NuGet/GitHub); explicit flags
  (`--package`, `--github`, `--repo`) skip all prompts
- **Advertised manifest**: `--repo`/`--github` require the
  repo to advertise a `tools` array in
  `.dotnet-install/.dotnet-install.json` (or name one with
  `--project`)
- **Single-file only**: only single-file native executables
  install (Native AOT or self-contained single-file / CLI
  tools v2). Managed or multi-file tools are refused with a
  pointer to `dotnet tool install`
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
dotnet test test/dotnet-install.Tests/dotnet-install.Tests.csproj
```

## When modifying the tool

- CLI is built with System.CommandLine — commands and
  options defined in `CommandLineBuilder.cs`
- Help output uses Markout serialization (`HelpWriter.cs`)
- The project targets `net10.0` with
  `PublishAot=true` — all code must be AOT-compatible
- Use STJ source generation for any JSON
  serialization (see `ManifestContext` in
  `GitSource.cs`)
- Error messages: `"error: <message>"` to stderr
- Status messages to stdout
- Only single-file (AOT or self-contained single-file)
  binaries install, placed directly in the install dir;
  managed/multi-file tools are refused (use `dotnet tool install`)
- Git repos cached at `~/.nuget/git-tools/<owner>/<repo>/`
- Project discovery order: `--project` > manifest >
  auto-detect Exe > file-based apps (≤12 → selector)
- See `DESIGN.md` for full architecture rationale
- Test with all three install modes and subcommands
