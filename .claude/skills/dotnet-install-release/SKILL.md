---
name: dotnet-install-release
description: >
  Publishing and release workflow for dotnet-install.
  Version bumps, CI, NuGet publish, GitHub Releases.
argument-hint: "[bump | publish | status]"
allowed-tools: Bash, Read, Glob, Grep
---

# dotnet-install release

You are helping the user publish a new release of
`dotnet-install`. This skill documents the full
release lifecycle.

## Version location

The version is in ONE place:

```text
src/dotnet-install/dotnet-install.csproj → <Version>X.Y.Z</Version>
```

`install.sh` resolves the latest version dynamically
from the GitHub Releases redirect — it does NOT
contain a hardcoded version.

## Release workflow

### 1. Bump version

Edit `<Version>` in `dotnet-install.csproj`.
Commit, push, merge PR.

### 2. Wait for CI

CI (`ci.yml`) runs on push to main. It:

- Builds on 5 platforms (linux-x64, linux-arm64,
  win-x64, win-arm64, osx-arm64)
- Publishes Native AOT binaries as tar.gz/zip
- Packs NuGet packages (pointer + RID-specific + any)
- Uploads all as artifacts

### 3. Find the CI run ID

```bash
gh run list -R richlander/dotnet-install \
  --branch main --workflow CI --limit 5
```

Copy the run ID of the successful CI run.

### 4. Dispatch the release

```bash
gh workflow run release.yml \
  -R richlander/dotnet-install \
  -f run_id=<CI_RUN_ID> \
  -f confirm=publish
```

This workflow:

- Downloads binary + package artifacts from the CI run
- Publishes all `.nupkg` to NuGet.org (OIDC auth)
- Publishes to GitHub Packages
- Creates a GitHub Release with tar.gz/zip binaries
- Release tag: `v<version>` (extracted from csproj)

### 5. Verify

```bash
# Check GitHub Release
gh release view vX.Y.Z -R richlander/dotnet-install

# Check NuGet (may take a few minutes to index)
dotnet package search dotnet-install --source https://api.nuget.org/v3/index.json

# Test install.sh (resolves latest automatically)
curl -sSf https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.sh | sh
```

## CI artifacts

CI produces these artifacts per successful run:

| Artifact | Contents |
| -------- | -------- |
| `binary-linux-x64` | `dotnet-install-linux-x64.tar.gz` |
| `binary-linux-arm64` | `dotnet-install-linux-arm64.tar.gz` |
| `binary-win-x64` | `dotnet-install-win-x64.zip` |
| `binary-win-arm64` | `dotnet-install-win-arm64.zip` |
| `binary-osx-arm64` | `dotnet-install-osx-arm64.tar.gz` |
| `package-pointer` | Meta-package `.nupkg` |
| `package-any` | CoreCLR fallback `.nupkg` |
| `package-linux-x64` | AOT `.nupkg` for linux-x64 |
| `package-linux-arm64` | AOT `.nupkg` for linux-arm64 |
| `package-win-x64` | AOT `.nupkg` for win-x64 |
| `package-win-arm64` | AOT `.nupkg` for win-arm64 |
| `package-osx-arm64` | AOT `.nupkg` for osx-arm64 |

## install.sh

`install.sh` resolves the latest version dynamically
by following the GitHub `/releases/latest` redirect.
It does NOT need updating on each release.

`install-source.sh` builds from the local source
checkout — for development use only.

`install-tool.sh` bootstraps via `dotnet tool install -g`
then graduates with `setup`.

## Versioning convention

- Patch bump (0.4.1 → 0.4.2): bug fixes, docs, skill updates
- Minor bump (0.4.x → 0.5.0): new commands or features
- Major bump: breaking changes (none yet)
