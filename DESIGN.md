# Design

Architecture and rationale for `dotnet-install`.

## Philosophy

`dotnet-install` relates to `dotnet tool install -g`
the way yarn relates to npm: same package registry (NuGet),
different installation model. The goal is a flat, transparent
layout — real binaries on PATH — matching `cargo install`,
`go install`, and Homebrew.

## Install layout

Tools land in `~/.dotnet/bin/` (override with `DOTNET_TOOL_BIN`).
This is deliberately separate from `~/.dotnet/tools/`, which
belongs to `dotnet tool install -g`.

| Directory          | Owner                    | Layout                     |
| ------------------ | ------------------------ | -------------------------- |
| `~/.dotnet/tools/` | `dotnet tool install -g` | Shim scripts → `.store/`   |
| `~/.dotnet/bin/`   | `dotnet-install`         | Real binaries, flat        |

### Single-file binaries (Native AOT)

Copied directly into the install directory. No subdirectories,
no shims — the binary *is* the tool.

### Multi-file binaries (managed)

Stored in `_<appname>/` subdirectory. A symlink (Unix) or
`.cmd` shim (Windows) in the install directory dispatches to
the real entry point — busybox-style. This keeps the top-level
directory clean while supporting managed tools that need
multiple files.

## Cross-platform

- **Unix**: symlinks and `chmod +x`
- **Windows**: `.cmd` shims and `.exe` detection

## Prompting model

Two dimensions: *how the source is specified* and *local vs remote*.

| Source | Local | Remote |
| ------ | ----- | ------ |
| Explicit (`--package`, `--github`) | Just does it | Just does it |
| Bare positional arg | Just does it | Prompts to confirm source |

Bare args prompt for remote sources as an anti-typosquatting
measure. Explicit flags signal intent and skip all prompts.
Multiple bare args also skip prompts (batch mode).

## Roll-forward

When a NuGet tool targets a runtime that isn't installed,
`dotnet tool install` silently installs but the tool crashes
on first launch with "You must install or update .NET."

`dotnet-install` handles this differently by source:

- **Remote (NuGet)**: auto-enables roll-forward with an
  informational message. The user chose to install a
  pre-built package — making it work is more important
  than strict version matching.
- **Local (source build)**: prompts the user, since they
  control the target framework and may prefer to update it.

The `--allow-roll-forward` flag suppresses the prompt for
local installs.

## SDK preflight

Building from source requires the .NET SDK. Rather than
letting `dotnet publish` fail with a confusing error (or
a "command not found"), the tool checks for the SDK
upfront and suggests `--package` as the SDK-free alternative.

## Git cache

GitHub repos are cloned to `~/.nuget/git-tools/<owner>/<repo>/`.
On re-install, `git fetch` updates the existing clone rather
than re-cloning. This matches the NuGet cache convention of
storing things under `~/.nuget/`.

## Project discovery

For GitHub repos with multiple projects, resolution order:

1. `--project` flag (explicit path)
2. `.dotnet-install.json` manifest in repo root
3. Auto-detect `Exe` projects in the repo
4. File-based apps (`.cs` with `#:property` directives)

If multiple candidates remain (≤12), an interactive
arrow-key selector is presented.

## Bootstrap graduation

Users can bootstrap `dotnet-install` itself via
`dotnet tool install -g dotnet-install`. After that,
`dotnet-install setup` self-installs from NuGet into
`~/.dotnet/bin/` and sheds the dotnet tool scaffolding —
a one-time graduation from the `.store/` model to the
flat layout.
