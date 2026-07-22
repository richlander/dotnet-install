# dotnet-install

Install .NET executables to PATH — like `cargo install` and `go install`.

Built with .NET, AOT-compiled, no runtime required.

```bash
dotnet-install .                              # Build & install current project
dotnet-install --package dotnetsay            # Install from NuGet
dotnet-install --github richlander/my-tool    # Install from GitHub
```

dotnet-install installs **single-file executables only** — Native AOT or
self-contained single-file tools (CLI tools v2). For managed or multi-file
tools, use `dotnet tool install` with the .NET SDK.

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
dotnet-install doctor --fix
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
dotnet-inspect  single-file
dotnet-install  single-file
dotnetsay       single-file
```

Use `--no-header` for scripting:

```bash
$ dotnet-install ls --no-header
dotnet-inspect  single-file
dotnet-install  single-file
dotnetsay       single-file
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

### Unsupported tools

dotnet-install only installs single-file native executables. If a NuGet
package is a managed (multi-file) tool, or a source build doesn't produce a
single file, it stops and points you at the SDK:

```bash
$ dotnet-install --package some-managed-tool
error: 'some-managed-tool' is not a single-file executable tool.

dotnet-install only installs single-file native tools (CLI tools v2).
Install this managed tool with the .NET SDK instead:
  dotnet tool install -g some-managed-tool
```

### Already installed as a .NET SDK tool

If the same command is already on your PATH — most often because it was
installed with `dotnet tool install -g` — dotnet-install refuses rather than
add a second copy that would shadow the first:

```bash
$ dotnet-install --package dotnet-inspect
error: a 'dotnet-inspect' command is already on your PATH:
  ~/.dotnet/tools/dotnet-inspect  (.NET SDK tool)

Installing it with dotnet-install would put a second copy on your PATH.
Uninstall the .NET SDK tool first, then re-run this command:
  dotnet tool uninstall -g dotnet-inspect
```

## How it works

Every tool is a single-file native executable, placed directly on PATH —
identical to Rust/Go. A small metadata sidecar tracks the install source
for updates:

```text
~/.dotnet/bin/
  mytool              # just the binary
  _mytool/
    .tool.json        # install source (for `update`)
```

## Project configuration

No new properties required, but the project must produce a single-file
executable. Enable Native AOT or self-contained single-file publishing:

```xml
<!-- Native AOT (recommended — produces a single native binary) -->
<PublishAot>true</PublishAot>

<!-- OR single-file CoreCLR -->
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
```

Managed or multi-file tools aren't supported here — install those with
`dotnet tool install`.

## Commands and options

```text
dotnet-install [<project-path>] [command] [options]

Commands:
  doctor         Configure shell PATH and check setup
  ls (list)      List installed tools
  update <tool>  Check for updates and reinstall
  rm (remove)    Remove installed tools

Options:
  --package <name[@version]>   Install a tool from NuGet
  --github <owner/repo[@ref]>  Install from a GitHub repository
  --project <path>             Path to .csproj within a git repo
  -o, --output <dir>           Installation directory (default: ~/.dotnet/bin/)
  --local-bin                  Install to ~/.local/bin/
  --require-sourcelink         Require SourceLink metadata
  --ssh                        Clone using SSH instead of HTTPS
  --no-header                  Suppress column headers (list command)
  -h, --help                   Show help
  --version                    Show version
```

## Design

See [dotnet/sdk#50747](https://github.com/dotnet/sdk/issues/50747)
for the full proposal.
