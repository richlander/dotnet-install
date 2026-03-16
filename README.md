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

```text
~/.dotnet/bin/
  mytool              # just the binary
```

### Multi-file apps

A subdirectory holds the files, with a symlink (Unix)
or CMD shim (Windows) on PATH:

```text
~/.dotnet/bin/
  otherapp → _otherapp/otherapp    # symlink
  _otherapp/                       # actual files
    otherapp
    otherapp.dll
    otherapp.deps.json
    ...
```

## Install

### SDK users

```bash
dotnet tool install -g dotnet-install
dotnet install setup
```

### Without the SDK

```bash
curl -sSf https://richlander.github.io/\
dotnet-install/install.sh | bash
```

The script downloads a platform-specific binary and runs
`dotnet-install setup` to configure your shell.

### Manual setup

If `~/.dotnet/bin` is not in your PATH after installing, run:

```bash
dotnet install setup
```

This will:

1. Create a symlink from `~/.dotnet/bin/dotnet-install`
   to the tool's location (if needed)
2. Add `~/.dotnet/bin` to your shell PATH
   (bash, zsh, or fish)

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
| --- | --- |
| `[project-path]` | Path to project or directory (default: `.`) |
| `setup` | Configure shell PATH and create self-link |
| `list` | List installed tools |
| `remove <tool>...` | Remove installed tools |
| `-o, --output` | Install directory (default: `~/.dotnet/bin/`) |
| `-h, --help` | Show help |

## Design

See [dotnet/sdk#50747](https://github.com/dotnet/sdk/issues/50747)
for the full proposal.
