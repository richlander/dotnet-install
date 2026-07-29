# dotnet-install

Install .NET executables to PATH — like `cargo install` and `go install`.

Built with .NET, AOT-compiled, no runtime required.

```bash
dotnet-install --package dotnet-inspect               # Install from NuGet
dotnet-install --github richlander/dotnet-runtimeinfo # Install from GitHub
dotnet-install                                        # Build & install what's here
```

dotnet-install installs **single-file executables only** — Native AOT or
self-contained single-file tools. For managed or multi-file tools, use
`dotnet tool install` with the .NET SDK.

## Install

No .NET required — downloads a self-contained native binary and configures
your PATH.

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
dotnet-install doctor --fix     # adds ~/.dotnet/bin to PATH, where installed tools go
```

The scripts above do this step for you.

### From source

For contributors or local development:

```bash
./install-source.sh
```

Builds from the local source tree via `dotnet publish` (requires the .NET SDK).

## Familiar for `cargo` and `go` users

If you install CLI tools with Rust or Go, you already know how this works.
The gesture is the same in all three: **name a source, get a command on
your PATH**, built clean and dropped in one flat directory.

| Toolchain | Command | Lands in |
| --- | --- | --- |
| Rust | `cargo install ripgrep` | `~/.cargo/bin/rg` |
| Go | `go install github.com/junegunn/fzf@latest` | `~/go/bin/fzf` |
| .NET | `dotnet-install --github richlander/dotnet-runtimeinfo` | `~/.dotnet/bin/dotnet-runtimeinfo` |

Real output from each, with the dependency churn elided:

```console
$ cargo install ripgrep
    Updating crates.io index
  Installing ripgrep v15.2.0
     Locking 46 packages to latest compatible versions
   Compiling memchr v2.8.3
   Compiling libc v0.2.189
                        ... 31 more crates ...
   Compiling ripgrep v15.2.0
    Finished `release` profile [optimized + debuginfo] target(s) in 15.97s
   Replacing /Users/rich/.cargo/bin/rg
    Replaced package `ripgrep v15.1.0` with `ripgrep v15.2.0` (executable `rg`)
```

```console
$ go install github.com/junegunn/fzf@latest
go: downloading github.com/junegunn/fzf v0.74.1
go: downloading github.com/charlievieth/fastwalk v1.0.14
                        ... 4 more modules ...
```

```console
$ dotnet-install --github richlander/dotnet-runtimeinfo
Fetching richlander/dotnet-runtimeinfo...
                        ... git fetch output ...
Installing dotnet-runtimeinfo to /Users/rich/.dotnet/bin
Publishing (Native AOT, Release)...
Installed dotnet-runtimeinfo → /Users/rich/.dotnet/bin/dotnet-runtimeinfo
```

All three install single-file native executables.

Installing from NuGet skips the build entirely: `--package` downloads a
prebuilt native binary, so there's no compile step at all.

## Why not `dotnet tool install`?

`dotnet tool install` requires the SDK and only installs from NuGet.

dotnet-install goes further:

- **No .NET required** — install and run Native AOT tools
  without the SDK or runtime
- **Uses the SDK if available** — build and install directly
  from local projects and GitHub repos
- **Update everything** — `dotnet-install update` checks all
  installed tools at once, like `npm update -g`
- **Just run it** — installed tools are on PATH; no `dotnet run`
  needed to find the executable
- **One gesture from source** — build and install in a single step, like
  `cargo install`. The SDK has no publish-pack-install path: you
  `dotnet pack`, then install from a local feed
- **Clean release build** — always does a publish-optimized build,
  just like `cargo install` and `go install`
- **Simple layout** — tools land in `~/.dotnet/bin/dotnet-inspect`,
  not `~/.dotnet/tools/.store/dotnet-inspect/0.7.2/...`

## Consuming tools

This is the daily-drive path, and it works the way `dotnet tool install` and
`cargo install` do: **you point at a source, you get a command on your PATH.**

```bash
dotnet-install --package dotnet-inspect                # from NuGet
dotnet-install --package dotnet-inspect@0.16.0         # pinned version
dotnet-install --github richlander/dotnet-runtimeinfo  # from GitHub
dotnet-install --repo https://github.com/richlander/dotnet-runtimeinfo
```

The source is always explicit for remote installs. With no arguments,
dotnet-install works on the current directory, the way `dotnet publish` does.

Everything lands in `~/.dotnet/bin`, flat — one binary per tool, plus a
hidden sidecar recording where it came from:

```text
~/.dotnet/bin/
  dotnet-inspect              # just the binary
  .tool.dotnet-inspect.json   # install source (for `update`)
```

No subdirectories, and nothing but binaries is visible in a normal
directory listing.
Use `-o <dir>` to install somewhere else, or `--local-bin` for `~/.local/bin`.

### NuGet packages

`--package` installs from NuGet. What matters is the **payload shape**, not
the package format: dotnet-install places native single-file executables, so
a managed tool is rejected with a pointer at `dotnet tool install -g`.

The tool generation is recorded in the package's `DotnetToolSettings.xml`
(not the NuGet package type, which is `DotnetTool` throughout):

| Generation | Marker | Supported |
| --- | --- | --- |
| v1 | `<DotNetCliTool Version="1">`, `Runner="dotnet"`, a `.dll` entry point | no — managed |
| v2 | `<DotNetCliTool Version="2">` with `RuntimeIdentifierPackages` | yes — resolves to a RID-specific native payload |
| v3 | `tools/manifest.json` | yes — RID index, bundle, and payload manifests |

A v2 package is a pointer: it names a RID-specific package per platform, and
dotnet-install redirects to the one matching yours. Those RID packages carry a
real native binary (`Runner="executable"`). Many v2 families also publish an
`any` fallback for unlisted platforms — that fallback is a managed v1 payload,
so resolving to it fails the same way a v1 tool does.

For [DotNetCliTool v3][v3] specifically, a pointer package resolves to the
right RID-specific payload for your platform, and a bundle package installs
its members together.

[v3]: https://github.com/dotnet/designs/blob/main/accepted/2026/dotnet-cli-tools-v3.md

### Repos

`--repo` takes a git URL or a local path; `--github owner/repo` is shorthand
for the GitHub URL. Either way the repo is built from source and the result is
installed. Pin with `--branch`, `--tag`, or `--rev`.

A repo must **advertise** what it publishes (see below) — or you name a project
with `--project`. dotnet-install won't guess which of a repo's projects is the
one you meant.

### List installed tools

```bash
$ dotnet-install ls
NAME                VERSION  TYPE         SOURCE
dotnet-inspect      0.16.0   single-file  nuget
dotnet-runtimeinfo  3.0.1    single-file  nuget
```

Use `--no-header` for scripting, or `--json` for structured output.

### Update all tools

```bash
$ dotnet-install update
dotnet-inspect (dotnet-inspect.osx-arm64 0.16.0)... up to date
dotnet-runtimeinfo (dotnet-runtimeinfo.osx-arm64 3.0.1)... up to date
```

`dotnet-install outdated` checks without installing.

### Remove tools

```bash
dotnet-install rm dotnet-inspect
```

## Producing tools

Building your own tool is closer to `cargo install`: a clean release build,
single-file output only, and a flat install location with no subdirectories.

It's also one step. The SDK has no gesture that goes from source to an
installed command — you `dotnet pack`, then `dotnet tool install` from a
local feed, producing a NuGet package you didn't want just to move a
binary onto your own PATH.

From inside a project directory, no arguments needed:

```bash
dotnet-install
```

A path works too, and `.` isn't special — any path is fine:

```bash
dotnet-install src/my-tool
dotnet-install ~/git/my-tool
dotnet-install app.cs            # file-based app
```

Either way dotnet-install looks for an executable project, scanning
subdirectories when nothing sits at the top level. Nothing found is an error:

```text
$ dotnet-install
error: no project file found in '/home/me/scratch'; see 'dotnet-install --help'
```

### Choosing between projects

When several projects match, **naming a path is what opts you into the
picker**. The bare command never starts a prompt you didn't ask for — it
reports the ambiguity and stops:

```text
$ dotnet-install
error: multiple executable projects found. Use --project to specify:
  src/tool-a/tool-a.csproj
  src/tool-b/tool-b.csproj

Or pass a path to choose interactively:
  dotnet-install .
```

```text
$ dotnet-install .
Multiple executable projects found. Select one:

  ❯ src/tool-a/tool-a.csproj
    src/tool-b/tool-b.csproj
  ↑↓/jk Navigate  Enter Select  Esc Cancel
```

A path only ever looks for a project — it doesn't read the repo's manifest.
That's what separates it from `--repo`, below.

### Advertising a tool or toolset

A repo can declare its official output — one tool or several — with a manifest
in a well-known directory: `.dotnet-install/.dotnet-install.json`, not bare at
the repo root (mirroring `.claude-plugin/` for skills).

Each entry in the `tools` array names a `name` (the command placed on `PATH`)
and a repo-relative `project` to build — modeled on Cargo's `[[bin]]` target:

```json
{
  "version": 3,
  "name": "my-tool",
  "tools": [
    { "name": "my-tool", "project": "src/my-tool/my-tool.csproj" }
  ]
}
```

Both fields are optional: a single-tool manifest that omits `project`
auto-detects the repo's sole executable, and `name` is derived from the
assembly name when omitted.

List several entries to advertise a set of tools — each must name its own
source:

```json
{
  "version": 3,
  "name": "my-toolset",
  "tools": [
    { "name": "tool-a", "project": "src/tool-a/tool-a.csproj" },
    { "name": "tool-b", "project": "src/tool-b/tool-b.csproj" }
  ]
}
```

The shape mirrors the DotNetCliTool v3 manifest, so the same toolset can be
published as a v3 bundle package. It is a **source-build variant** of that
format: v3's package-level fields (a RID index, or a bundle of package IDs)
describe published packages and have no meaning here.

An entry names exactly one source. Alongside `project`, an entry may name a
NuGet `package` or another `repository`, so a toolset can be assembled from
more than what this repo builds:

| Source | Means | Optional |
| --- | --- | --- |
| `project` | a repo-relative `.csproj` or file-based app (`.cs`) | |
| `package` | a NuGet package id | `version` |
| `repository` | `owner/repo` on GitHub | `ref` |

All three can be mixed in one toolset:

```json
{
  "version": 3,
  "name": "my-toolset",
  "tools": [
    { "name": "hello-cs",   "project": "tools/hello-cs/hello-cs.csproj" },
    { "name": "hello-file", "project": "tools/hello-file/hello-file.cs" },
    { "package": "dotnet-runtimeinfo" },
    { "repository": "richlander/dotnet-inspect", "ref": "v0.16.0" }
  ]
}
```

That makes a manifest a way to describe a whole environment, not just this
repo's output — a team can put the tools everyone needs in one file and have
new machines catch up with `dotnet-install --repo .`.

Each tool still records where **it** came from, so `update` pulls each one from
its own source rather than from the repo that listed it. The whole manifest is
validated before anything is installed, so a typo in the last entry can't leave
you with a half-installed toolset.

The legacy `exe`, `project`, and `bundle` fields still work.

Once a repo advertises its toolset, `--repo` builds and installs the whole set:

```bash
dotnet-install --repo .
```

This is the maintainer's gesture, and it's what the manifest is for:

| | |
| --- | --- |
| `dotnet-install .` | finds *a project* — you get whatever is there |
| `dotnet-install --repo .` | builds what the repo *publishes* — one tool or many |

`--repo` saves you from remembering the path to the project, and builds
multiple tools in one gesture — the DotNetCliTool v3 bundle concept, applied to
a source tree. Installation stops at the first failure, leaving
already-installed tools in place. An explicit `--project` overrides the toolset
and installs a single tool.

### Project configuration

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

## Unsupported tools

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

## Commands and options

```text
dotnet-install [<project>] [command] [options]

Options:
  --package <name[@version]>   Install a tool from NuGet
  --github <owner/repo[@ref]>  Install from a GitHub repository
  --repo <url|path>            Build a repo (git URL or local path) and install its advertised tools
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

See [DESIGN.md](DESIGN.md) for architecture and rationale, and
[dotnet/sdk#50747](https://github.com/dotnet/sdk/issues/50747) for the full
proposal.
