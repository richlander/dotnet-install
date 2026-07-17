# Design

Architecture and rationale for `dotnet-install`.

## Philosophy

`dotnet-install` relates to `dotnet tool install -g`
the way yarn relates to npm: same package registry (NuGet),
different installation model. The goal is a flat, transparent
layout — real binaries on PATH — matching `cargo install`,
`go install`, and Homebrew.

## Scope: single-file executables only

`dotnet-install` installs **only single-file native executables** —
Native AOT binaries and self-contained single-file tools (CLI tools v2).
It does not install managed (multi-file) tools or run tools under
`dotnet exec`. When a NuGet package is a managed tool, or a source build
doesn't produce a single file, the tool stops and directs the user to
`dotnet tool install`, which owns that model.

This keeps the layout flat and the runtime story simple: the binary *is*
the tool, with no host dispatch, entry-point resolution, or roll-forward.

## Install layout

Tools land in `~/.dotnet/bin/` (override with `DOTNET_TOOL_BIN`).
This is deliberately separate from `~/.dotnet/tools/`, which
belongs to `dotnet tool install -g`.

| Directory          | Owner                    | Layout                     |
| ------------------ | ------------------------ | -------------------------- |
| `~/.dotnet/tools/` | `dotnet tool install -g` | Shim scripts → `.store/`   |
| `~/.dotnet/bin/`   | `dotnet-install`         | Real binaries, flat        |

The single-file binary is copied directly into the install directory.
No subdirectories, no shims — the binary *is* the tool. A `_<appname>/`
sidecar directory holds only `.tool.json` metadata that records the
install source for `update`.

## Cross-platform

- **Unix**: `chmod +x` on the placed binary
- **Windows**: `.exe` detection

## Prompting model

Two dimensions: *how the source is specified* and *local vs remote*.

| Source | Local | Remote |
| ------ | ----- | ------ |
| Explicit (`--package`, `--github`) | Just does it | Just does it |
| Bare positional arg | Just does it | Prompts to confirm source |

Bare args prompt for remote sources as an anti-typosquatting
measure. Explicit flags signal intent and skip all prompts.
Multiple bare args also skip prompts (batch mode).

## SDK preflight

Building from source requires the .NET SDK. Rather than
letting `dotnet publish` fail with a confusing error (or
a "command not found"), the tool checks for the SDK
upfront and suggests `--package` as the SDK-free alternative.
It also warns (via `dotnet --list-runtimes`) when a project
targets a framework newer than any installed runtime.

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
