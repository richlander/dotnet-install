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
No subdirectories, no shims — the binary *is* the tool. Alongside it sits
a `.tool.<name>.json` sidecar recording the install source for `update`.

The sidecar is a flat dotfile rather than a per-tool directory, so the
install directory stays one-file-per-tool and a plain `ls` shows only
commands. Installs predating this layout kept the same metadata as
`_<name>/.tool.json`; those are still read, and are rewritten to the flat
form the next time the tool is installed or updated. A `_<name>/`
directory holding anything more than that sidecar is a pre-redesign
managed install, which is how `ls` and `info` still label those honestly.

## Cross-platform

- **Unix**: `chmod +x` on the placed binary
- **Windows**: `.exe` detection

## Gesture model

You point at a source; the tool lands on your PATH. This is the
`cargo install` / `go install` contract, and it holds for every
source:

| Source | Example |
| ------ | ------- |
| NuGet package | `--package dotnet-inspect` |
| Repo (URL or path) | `--repo <url\|path>`, `--github owner/repo` |
| Specific project | `--project src/my-tool` |
| Whatever is here | `dotnet-install` (or any path) |

All of them install to `~/.dotnet/bin`. There is exactly one install
location, and `-o` overrides it like any other tool's output flag —
nothing about a repo's contents changes where a tool lands.

With no arguments the current directory is the source, the way
`dotnet publish` works. `--help` prints help.

### Path vs. repo

The two source-build gestures are deliberately kept apart:

- `dotnet-install <path>` (or no argument, meaning the current directory)
  looks for **a project**. It never reads the manifest, so it behaves the
  same in any directory.
- `dotnet-install --repo <path>` builds what the repo **advertises** in
  `.dotnet-install/.dotnet-install.json` — one tool or a whole toolset.

Splitting them on "does it read the manifest" leaves no overlap to
explain, and means neither gesture changes behavior based on a file the
user may not know exists.

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

For a path with multiple projects, resolution order:

1. `--project` flag (explicit path)
2. `.dotnet-install/.dotnet-install.json` `tools` array — the toolset the repo
   advertises; every listed tool is built and installed together (see below)
3. Project files or file-based apps sitting directly in the directory
4. A recursive scan for executable projects (`OutputType=Exe`/`WinExe`, or an
   SDK that implies one), skipping build output and vendored trees — so pointing
   at a repo root resolves `src/my-tool/my-tool.csproj` instead of failing

If multiple candidates remain, resolution is ambiguous and the tool stops.
An arrow-key selector (≤12 candidates, shallowest first) is offered only when
the user named a path — `dotnet-install .` — never for the bare command.

Interactivity is opt-in for the same reason the install location is: a tool
should not start a prompt the user did not ask for. The bare command is the
one most likely to appear in a script or an agent loop, so it always resolves
or fails.

## Advertised toolsets

The `.dotnet-install.json` manifest appears in two places, with the same
filename and schema:

- **Colocated** — in a directory you point the tool at directly (a project
  directory / local path).
- **Repo** — at `.dotnet-install/.dotnet-install.json`, read by the repo gesture:
  `--github` or `--repo <url|path>`. The repo root itself is never scanned — only
  the `.dotnet-install/` directory. This mirrors `.claude-plugin/` for skills,
  where the advertise manifest lives in a well-known directory rather than bare
  at the root.

`--repo`/`--github` require the repo to advertise a `tools` array unless
`--project` names one explicitly. A bare path does not consult the manifest at
all; it scans for an executable project.

A repo advertises its toolset with a `tools` array. Each entry names a `name`
(the command placed on `PATH`) and a repo-relative `project` to build. This is
modeled on Cargo's `[[bin]]` target (`name` + `path`). A single entry is a
one-tool repo:

```json
{
  "version": 3,
  "name": "my-tool",
  "tools": [
    { "name": "my-tool", "project": "src/my-tool/my-tool.csproj" }
  ]
}
```

Both fields are optional. `project` is present only in source scenarios (it is
absent once a tool is published as a prebuilt package), and when a single-tool
manifest omits it the repo's sole executable project is auto-detected (Go-style).
`name` is derived from the project's assembly name when omitted. A multi-tool
manifest cannot auto-detect, so every entry must name a source. An entry names
exactly one of three:

| Source | Means | Optional |
| --- | --- | --- |
| `project` | a repo-relative `.csproj` or file-based app (`.cs`) | |
| `package` | a NuGet package id | `version` |
| `repository` | `owner/repo` on GitHub | `ref` |

```json
{
  "version": 3,
  "name": "my-toolset",
  "tools": [
    { "name": "tool-a", "project": "src/tool-a/tool-a.csproj" },
    { "package": "dotnet-runtimeinfo" },
    { "repository": "richlander/dotnet-inspect", "ref": "v0.16.0" }
  ]
}
```

This mirrors the tool-bundle concept in the DotNetCliTool v3 design, adapted to
build-from-source: `project` is the "local" flavor v3 has no need for, since a
published bundle can only reference packages. Supporting all three in one array
is what lets a manifest describe an environment rather than just this repo's
output.

Installing from the repo (`--github` or `--repo`) installs every entry,
recording per-tool provenance so each updates from its **own** source, not from
the repo that listed it. Every entry is resolved before any is installed, so a
manifest error fails the whole set rather than leaving it half-applied; a
failure during installation still stops at that point and leaves the tools
already installed in place. An explicit `--project` overrides the toolset.

Because an entry can name another repo, and that repo can advertise a manifest
of its own, resolution is recursive. Repos being installed are tracked so two
that reference each other fail with a cycle error rather than cloning forever.

The legacy `exe`, `project`, and `bundle` fields remain readable and are
normalized onto the `tools` array, so existing manifests keep working.

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
