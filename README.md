# dotnet-install

Install .NET executables to PATH — like `cargo install` and `go install`.

Built with .NET, AOT-compiled, no runtime required.

```bash
dotnet-install .                                  # Build & install current project
dotnet-install --package dotnet-inspect           # Install from NuGet
dotnet-install --github richlander/dotnet-install # Install from GitHub
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
dotnet-install --package dotnet-inspect
dotnet-install --package dotnet-inspect@0.16.0

# From GitHub
dotnet-install --github richlander/dotnet-install
```

### List installed tools

```bash
$ dotnet-install ls
NAME            VERSION  TYPE         SOURCE
dotnet-inspect  0.16.0   single-file  nuget
dotnet-install  0.8.0    single-file  github
```

Use `--no-header` for scripting:

```bash
$ dotnet-install ls --no-header
dotnet-inspect  0.16.0   single-file  nuget
dotnet-install  0.8.0    single-file  github
```

### Update all tools

```bash
$ dotnet-install update
dotnet-inspect (dotnet-inspect 0.16.0)... up to date
dotnet-install (richlander/dotnet-install 40de536)... up to date
```

### Remove tools

```bash
dotnet-install rm dotnet-inspect
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
dotnet-install [<project>] [command] [options]

Options:
  --package <name[@version]>   Install a tool from NuGet
  --github <owner/repo[@ref]>  Install from a GitHub repository
  --git <url>                  Install from a git URL
  --branch <name>              Git branch to track (updatable)
  --tag <name>                 Git tag to install (pinned)
  --rev <sha>                  Git commit SHA to install (pinned)
  --path, --project <path>     Path to project (or sub-path within a git repo)
  -o, --output <dir>           Installation directory (overrides default)
  --local-bin                  Install to ~/.local/bin/ instead of ~/.dotnet/bin/
  --ssh                        Clone using SSH instead of HTTPS
  --require-sourcelink         Require SourceLink metadata in installed assemblies
  -h, --help                   Show help and usage information
  --version                    Show version information

Commands:
  doctor                Check environment setup
  config <key> <value>  View and update settings
  ls                    List installed tools
  update <tool>         Check for updates and reinstall
  rm <tool>             Remove installed tools
  search <query>        Search NuGet for tool packages
  info <tool>           Show detailed information about an installed tool
  outdated              Check for available updates without installing
  completion <shell>    Generate shell completion script
  env                   Print environment information
  skill                 Print the AI skill definition for this tool
```

## Design

See [dotnet/sdk#50747](https://github.com/dotnet/sdk/issues/50747)
for the full proposal.
