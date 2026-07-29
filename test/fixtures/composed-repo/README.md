# composed-repo fixture

A stand-in for a repo that advertises a toolset composed of every entry kind a
manifest supports. It exists so the composition path has a fixed, reviewable
example instead of one assembled by hand in `/tmp`.

`.dotnet-install/.dotnet-install.json` names four tools:

| Entry | Kind | Resolves to |
| --- | --- | --- |
| `greet` | `project` (file-based app) | `src/greet.cs` |
| `hello` | `project` (csproj) | `src/hello/hello.csproj` |
| `dotnet-inspect` | `repository` | another repo, built from source |
| `dotnet-runtimeinfo` | `package` | NuGet |

The first two are the repo's own source, the last two are external. Installing
this manifest produces four commands whose recorded sources differ
(`local`, `local`, `github`, `nuget`), and each then updates from where it
actually came from.

## Tests

`ComposedRepoFixtureTests` reads this manifest and checks that all four entries
resolve to the right kind. Those tests are offline: they resolve entries, they
do not clone, download, or build.

## End-to-end

Resolution being right is not the same as installation working, and the two
external entries can only be proven against the real network. To install the
whole manifest for real:

```bash
DOTNET_TOOL_BIN=/tmp/composed-bin dotnet run --project src/dotnet-install -- \
  --repo test/fixtures/composed-repo
DOTNET_TOOL_BIN=/tmp/composed-bin dotnet run --project src/dotnet-install -- ls
```

Expect four tools listed with the four source types above. This is deliberately
not part of `dotnet test` — it clones a repo, downloads a package, and runs two
Native AOT publishes.
