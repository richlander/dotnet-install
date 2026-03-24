# dotnet-install

Install .NET executables to PATH — like `cargo install` and `go install`.

Built with .NET, AOT-compiled, no runtime required.

```bash
dotnet-install .                              # Build & install current project
dotnet-install --package dotnetsay            # Install from NuGet
dotnet-install --github richlander/my-tool    # Install from GitHub
```

## Why not `dotnet tool install`?

`dotnet tool install` requires the SDK, only installs from NuGet, and every
tool runs as a managed DLL under `dotnet exec`.

dotnet-install goes further:

- **No .NET required** — install and run Native AOT tools
  without the SDK or runtime
- **Uses the SDK if available** — build and install directly
  from local projects and GitHub repos
- **Update everything** — `dotnet-install update` checks all
  installed tools at once, like `npm update`
- **Just run it** — installed tools are on PATH; no `dotnet run`
  needed to find the executable
- **Clean release build** — always does a publish-optimized build,
  just like `cargo install` and `go install`
- **Run without installing** — `dotnet-install run dotnetsay`
  executes a NuGet tool directly, like `dnx`
- **Roll-forward at install** — prompts once if the tool needs
  a newer runtime, instead of failing on first run
- **Simple layout** — tools land in `~/.dotnet/bin/dotnet-inspect`,
  not `~/.dotnet/tools/.store/dotnet-inspect/0.7.2/...`

## Install

No .NET required — downloads a self-contained native binary.

**Linux / macOS:**

```bash
curl --proto '=https' --tlsv1.2 -sSfL \
  https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.sh | sh
```

**Windows (PowerShell):**

```powershell
irm https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.ps1 | iex
```

### Already have the SDK?

```bash
dotnet tool install -g dotnet-install
dotnet-install setup
```

### From source

For contributors or local development:

```bash
./install-source.sh
```

Builds from the local source tree via `dotnet publish` (requires the .NET SDK).

## Usage

### Install tools

```bash
# From a local project
dotnet-install .
dotnet-install src/my-tool

# From NuGet
dotnet-install --package dotnetsay
dotnet-install --package dotnet-outdated-tool

# From GitHub
dotnet-install --github richlander/dotnet-runtimeinfo
```

### List installed tools

```bash
$ dotnet-install ls
NAME            TYPE
dotnet-inspect  NAOT
dotnet-install  NAOT
dotnetsay       CoreCLR
```

Use `--no-header` for scripting:

```bash
$ dotnet-install ls --no-header
dotnet-inspect  NAOT
dotnet-install  NAOT
dotnetsay       CoreCLR
```

### Update all tools

```bash
$ dotnet-install update
dotnet-inspect (local 99c56b9)... uncommitted changes, rebuilding
dotnet-runtimeinfo (richlander/dotnet-runtimeinfo 40de536)... up to date
dotnetsay (dotnetsay 3.0.3)... up to date
```

### Remove tools

```bash
dotnet-install rm dotnetsay
```

### Runtime roll-forward

When installing a managed tool that targets a .NET version you don't
have, dotnet-install will prompt to enable roll-forward:

```bash
$ dotnet-install --package dotnetsay@2.0.0
dotnetsay requires .NET 2.1.0-preview2-26406-04 (not installed). Enable roll-forward? [Y/n] y
Installed dotnetsay (2.0.0) → ~/.dotnet/bin/dotnetsay
```

Or pass `--allow-roll-forward` directly:

```bash
dotnet-install --package dotnetsay@2.0.0 --allow-roll-forward
```

## How it works

### Single-file apps (Native AOT / PublishSingleFile)

The binary is placed directly — identical to Rust/Go:

```text
~/.dotnet/bin/
  mytool              # just the binary
```

### Managed apps (CoreCLR)

A subdirectory holds the files. A symlink to `dotnet-install` on
PATH enables **BusyBox dispatch** — the tool name is detected from
`argv[0]` and routed to `dotnet exec` with the correct entry point
and roll-forward settings:

```text
~/.dotnet/bin/
  dotnetsay → dotnet-install   # symlink (BusyBox dispatch)
  _dotnetsay/                  # actual files
    dotnetsay.dll
    .tool.json                 # entry point + roll-forward config
    ...
```

## Project configuration

No new properties required. Existing MSBuild properties control the output:

```xml
<!-- Native AOT (recommended — produces a single native binary) -->
<PublishAot>true</PublishAot>

<!-- OR single-file CoreCLR -->
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>

<!-- OR plain executable (multi-file, uses BusyBox dispatch) -->
<OutputType>Exe</OutputType>
```

## Commands and options

```text
dotnet-install [<project-path>] [command] [options]

Commands:
  setup          Configure shell PATH and create self-link
  ls (list)      List installed tools
  update <tool>  Check for updates and reinstall
  rm (remove)    Remove installed tools

Options:
  --package <name[@version]>   Install a tool from NuGet
  --github <owner/repo[@ref]>  Install from a GitHub repository
  --project <path>             Path to .csproj within a git repo
  -o, --output <dir>           Installation directory (default: ~/.dotnet/bin/)
  --local-bin                  Install to ~/.local/bin/
  --allow-roll-forward         Allow tool to run on a newer .NET runtime
  --require-sourcelink         Require SourceLink metadata
  --ssh                        Clone using SSH instead of HTTPS
  --no-header                  Suppress column headers (list command)
  -h, --help                   Show help
  --version                    Show version
```

## Design

See [dotnet/sdk#50747](https://github.com/dotnet/sdk/issues/50747)
for the full proposal.
