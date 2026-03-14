# dotnet-install

Install .NET executables to PATH — like `cargo install` and `go install`.

```bash
dotnet install                          # Build & install current project
dotnet install src/my-tool              # Install from subdirectory
dotnet install -o ~/tools               # Install to custom location
```

## What it does

`dotnet install` performs two operations:

1. **Publishes** a Release build from source (`dotnet publish -c Release`)
2. **Places** the binary in `~/.dotnet/bin/` (or a custom directory)

### Single-file apps (Native AOT / PublishSingleFile)

The binary is placed directly — identical to Rust/Go:

```
~/.dotnet/bin/
  mytool              # just the binary
```

### Multi-file apps

A subdirectory holds the files, with a symlink (Unix) or CMD shim (Windows) on PATH:

```
~/.dotnet/bin/
  otherapp → _otherapp/otherapp    # symlink
  _otherapp/                       # actual files
    otherapp
    otherapp.dll
    otherapp.deps.json
    ...
```

## Install

```bash
dotnet tool install -g dotnet-install
```

Ensure `~/.dotnet/bin` is on your PATH.

## Project configuration

No new properties required. Existing MSBuild properties control the output:

```xml
<!-- Native AOT (recommended — produces a single native binary) -->
<PublishAot>true</PublishAot>

<!-- OR single-file CoreCLR -->
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>

<!-- OR plain executable (multi-file, uses symlink/shim) -->
<OutputType>Exe</OutputType>
```

## Options

| Option | Description |
|---|---|
| `[project-path]` | Path to project file or directory (default: `.`) |
| `-o, --output` | Installation directory (default: `~/.dotnet/bin/`) |
| `-h, --help` | Show help |

## Design

See [dotnet/sdk#50747](https://github.com/dotnet/sdk/issues/50747) for the full proposal.
