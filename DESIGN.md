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

## Gesture model

Two directions, which also determine the install target:

| Request | Example | Target |
| ------- | ------- | ------ |
| Outside (explicit source) | `--repo`, `--github`, `--project`, `--package` | Global (`~/.dotnet/bin`) |
| Inside (bare `.`, advertised) | `dotnet-install .` | Local (`./.dotnet/bin`) |

Explicit flags signal intent and install globally. A bare `.` honors a repo's
advertised tooling and installs locally. With no manifest, `.` degrades to an
outside request: interactive (a terminal) installs the scanned project globally;
non-interactive (piped) errors and points at `--project .`. No arguments prints
help.

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
2. `.dotnet-install/.dotnet-install.json` `tools` array — the toolset the repo
   advertises; every listed tool is built and installed together (see below)
3. Auto-detect `Exe` projects in the repo
4. File-based apps (`.cs` with `#:property` directives)

If multiple candidates remain (≤12), an interactive
arrow-key selector is presented.

## Advertised toolsets

The `.dotnet-install.json` manifest appears in two places, with the same
filename and schema:

- **Colocated** — in a directory you point the tool at directly (a project
  directory / local path).
- **Repo** — at `.dotnet-install/.dotnet-install.json`, read when installing via
  the repo gesture: `--github`, `--repo <url|path>`, or a local checkout
  (`dotnet-install .`). The repo root itself is never
  scanned — only the `.dotnet-install/` directory. This mirrors `.claude-plugin/`
  for skills, where the advertise manifest lives in a well-known directory rather
  than bare at the root.

Direction determines the install target. An **outside** request — `--repo`,
`--github`, `--project`, `--package` — points at something and installs
**globally** (`~/.dotnet/bin`). An **inside** request — a bare `.` when the
directory advertises tools — installs **locally** (`./.dotnet/bin`), so a repo's
own dev tooling never pollutes the global set. `--repo`/`--github` require the
repo to advertise a `tools` array unless `--project` names one explicitly;
`.` with no manifest degrades to an outside request (interactive global install,
or an error when piped).

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
  ],
  "update": { "type": "nuget", "package": "my-tool" }
}
```

Both fields are optional. `project` is present only in source scenarios (it is
absent once a tool is published as a prebuilt package), and when a single-tool
manifest omits it the repo's sole executable project is auto-detected (Go-style).
`name` is derived from the project's assembly name when omitted. A multi-tool
manifest cannot auto-detect, so every entry must name a `project`:

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

This mirrors the tool-bundle concept in the DotNetCliTool v3 design, adapted
to build-from-source: the entries reference projects in the repo (the "local"
flavor) rather than NuGet package ids. Installing from the repo
(`--github`, `--repo`, or a local checkout — `dotnet-install .`) builds and
installs every entry, recording per-tool provenance so each updates
independently. Installation stops at the first failure and leaves
already-installed tools in place. An explicit `--project` overrides the toolset.

The `update` channel is honored only for **outside/global** installs. An
**inside** install (`dotnet-install .` → `./.dotnet/bin`) always rebuilds from
the checkout, so no update channel is recorded — you refresh by re-running
against the repo.

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
