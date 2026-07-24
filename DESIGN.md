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
2. `.dotnet-install/.dotnet-install.json` `bundle` — a toolset the repo
   advertises; every listed project is built and installed together (see below)
3. `.dotnet-install/.dotnet-install.json` `project` field
4. Auto-detect `Exe` projects in the repo
5. File-based apps (`.cs` with `#:property` directives)

If multiple candidates remain (≤12), an interactive
arrow-key selector is presented.

## Bundles (repo toolsets)

The `.dotnet-install.json` manifest appears in two places, with the same
filename and schema:

- **Colocated** — in a directory you point the tool at directly (a project
  directory / local path). Describes that one tool (`exe`, `update`).
- **Repo** — at `.dotnet-install/.dotnet-install.json`, read when installing via
  the repo gesture (`--github`/`--git`). The repo root itself is never scanned —
  only the `.dotnet-install/` directory. This mirrors `.claude-plugin/` for
  skills, where the advertise manifest lives in a well-known directory rather
  than bare at the root.

A repo advertises a set of tools by listing repo-relative projects in a
`bundle` array in `.dotnet-install/.dotnet-install.json`:

```json
{
  "version": 3,
  "name": "my-toolset",
  "bundle": [
    { "project": "src/tool-a/tool-a.csproj" },
    { "project": "src/tool-b/tool-b.csproj" }
  ]
}
```

This mirrors the tool-bundle concept in the DotNetCliTool v3 design, adapted
to build-from-source: the entries reference projects in the repo (the "local"
flavor) rather than NuGet package ids. Installing from the repo root
(`--github`, `--git`, or a local checkout) builds and installs every entry,
recording per-tool provenance so each updates independently. Installation stops
at the first failure and leaves already-installed tools in place. An explicit
`--project` overrides the bundle.

## DotNetCliTool v3 packages

When a NuGet package carries a `tools/manifest.json` with `"version": 3`,
`dotnet-install` treats it as a [DotNetCliTool v3][v3] package and dispatches
on its shape:

- **Pointer (index)** — the manifest lists RID-specific packages in an `index`.
  The installer picks the best match for the current platform using the RID
  fallback chain (exact RID → portable → `any`) and redirects to that package
  at the same version.
- **Pointer (bundle)** — the manifest lists other packages in a `bundle`. Each
  is installed in turn (an exact-match range like `[9.0.661903]` pins the
  version; a bare id installs latest). Installation stops at the first failure
  and leaves already-installed members in place.
- **RID-specific** — the manifest has a `descriptor` (its RID/id) and
  `commands`. Native single-file payloads under `tools/<rid>/` are placed into
  the install directory like any other single-file tool.

Consistent with the single-file scope, a v3 payload that resolves to the
managed `any` fallback (a command with `"runner": "dotnet"` or a `.dll`
entry point) is not installed; the tool directs the user to
`dotnet tool install`. Package-controlled RIDs and entry-point names are
validated against path traversal before any file is placed.

[v3]: https://github.com/dotnet/designs/blob/main/accepted/2026/dotnet-cli-tools-v3.md

## Bootstrap

Install `dotnet-install` itself with the SDK via
`dotnet tool install -g dotnet-install`. It stays a
managed .NET tool; run `dotnet-install doctor --fix`
to add `~/.dotnet/bin/` to PATH. The single-file tools
it installs land there.
