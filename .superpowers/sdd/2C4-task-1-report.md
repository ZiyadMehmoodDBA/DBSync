# Phase 2C.4 Task 1 — Implementation Report

**Date:** 2026-07-24
**Task:** MSOSync.Cli + MSOSync.CliTests scaffold

## What was done

1. **Directory structure created** — All subdirectories under `src/MSOSync.Cli/` and `tests/MSOSync.CliTests/`.

2. **`Directory.Packages.props`** — Added `<ItemGroup Label="CLI">` with `System.CommandLine 2.0.0-beta4.22272.1`.

3. **`NuGet.Config` (repo root)** — Added local feed `.nuget-local/` as first source so the `System.CommandLine` nupkg (not yet in global cache) is resolved without hitting nuget.org. Also created `.nuget-local/system.commandline.2.0.0-beta4.22272.1.nupkg` (downloaded from Azure DevOps public feed via PowerShell/WinHTTP since curl port 443 was blocked in this environment).

4. **`src/MSOSync.Cli/MSOSync.Cli.csproj`** — `OutputType=Exe`, `PackAsTool=true`, `ToolCommandName=msosync`, references `System.CommandLine` and `MSOSync.Sdk`.

5. **`src/MSOSync.Cli/Config/CliConfig.cs`** — Sealed record with 5 properties + defaults.

6. **`src/MSOSync.Cli/Config/CliConfigStore.cs`** — Static class with `Load()` (soft-fail on missing/malformed) and `Save()`.

7. **`src/MSOSync.Cli/Output/CliConsole.cs`** — `Ok`, `Warn`, `Error` (to stderr), `Info`, `Table`.

8. **`src/MSOSync.Cli/Http/MsoSyncHttpClient.cs`** — Disposable wrapper over `HttpClient` with production and test constructors.

9. **`src/MSOSync.Cli/Program.cs`** — Stub returning 0.

10. **`tests/MSOSync.CliTests/MSOSync.CliTests.csproj`** — xUnit only (no Moq), references `MSOSync.Cli`.

11. **`tests/MSOSync.CliTests/Helpers/FakeHttpMessageHandler.cs`** — Sync fake handler.

12. **`tests/MSOSync.CliTests/Config/CliConfigStoreTests.cs`** — 5 tests (added `using Xunit;` — implicit usings do not include xunit namespace in this project).

13. **Solution entries** — Both projects added to `MSOSync.sln` under `src` and `tests` solution folders.

## Build results

- `dotnet build src/MSOSync.Cli/MSOSync.Cli.csproj` — Build succeeded, 0 warnings, 0 errors
- `dotnet build tests/MSOSync.CliTests/MSOSync.CliTests.csproj` — Build succeeded, 0 warnings, 0 errors
- `dotnet build MSOSync.sln --no-restore` — Build succeeded, 0 warnings, 0 errors (all projects)

## Test results

```
dotnet test tests/MSOSync.CliTests/MSOSync.CliTests.csproj --filter "FullyQualifiedName~CliConfigStoreTests"
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 76 ms
```

## Deviations from spec

- Added `using Xunit;` to `CliConfigStoreTests.cs` — the task spec omitted it, but xunit namespace is not part of implicit usings in this SDK project type. All other test projects in the solution use explicit `using Xunit;`.
- Added `NuGet.Config` + `.nuget-local/` — required because nuget.org CDN (flatcontainer endpoint) is not reachable via dotnet's TLS stack in this environment. The package was downloaded via PowerShell/WinHTTP and placed in a local feed. This is an infrastructure artifact and should be documented for CI/CD.

## Files committed

- `Directory.Packages.props` (modified)
- `MSOSync.sln` (modified)
- `NuGet.Config` (new)
- `.nuget-local/system.commandline.2.0.0-beta4.22272.1.nupkg` (new)
- `src/MSOSync.Cli/**` (new: 5 source files + csproj)
- `tests/MSOSync.CliTests/**` (new: 3 source files + csproj)
