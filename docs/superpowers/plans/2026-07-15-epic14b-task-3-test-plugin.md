# Epic 14B — Task 3: Update MSOSync.TestPlugin

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Update `MSOSync.TestPlugin` to implement `IPlugin` via `PluginBase`. Update `plugin.json` with the new 14B manifest fields. Rebuild the DLL and copy it to `TestAssets`.

**Architecture:** `MSOSync.TestPlugin` is the test double used by integration tests. It references ONLY `MSOSync.Sdk` — it must never reference `MSOSync.Plugin` or any host project. The built DLL is a pre-compiled artifact stored in `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/`. After this task, loading the TestPlugin also verifies that `MSOSync.Sdk.dll` resolves through the host's `AssemblyLoadContext` (not the plugin's isolated context).

**Tech Stack:** C# 13 / .NET 9

## Global Constraints

- `MSOSync.TestPlugin` must reference only `MSOSync.Sdk` — no MSOSync.Plugin, no MSOSync.App
- `TreatWarningsAsErrors=true`
- DLL output must be copied to `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/`
- `plugin.json` needs: `manifestVersion`, `sdkVersion`, `apiVersion`, `startupOrder` fields added

## Files

**Modify:**
- `tests/MSOSync.TestPlugin/MSOSync.TestPlugin.csproj` — add ProjectReference to MSOSync.Sdk
- `tests/MSOSync.TestPlugin/TestPlugin.cs` — implement IPlugin via PluginBase; record lifecycle calls
- `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/plugin.json` — add new manifest fields
- `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/MSOSync.TestPlugin.dll` — replace with rebuilt DLL

## Interfaces

**Consumes:**
- `IPlugin`, `IPluginContext` from `MSOSync.Sdk.Abstractions` (Task 1)
- `PluginBase` from `MSOSync.Sdk.Hosting` (Task 1)

**Produces:**
- `MSOSync.TestPlugin.TestPlugin : PluginBase` — concrete class used by integration tests (Tasks 8, 9)
- `plugin.json` with `sdkVersion: "1.0"`, `apiVersion: "1"`, `startupOrder: 1000`, `manifestVersion: 1`

---

- [ ] **Step 1: Update `tests/MSOSync.TestPlugin/MSOSync.TestPlugin.csproj`**

Current content has no references. Add `MSOSync.Sdk` reference:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>MSOSync.TestPlugin</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Update `tests/MSOSync.TestPlugin/TestPlugin.cs`**

Current content is a bare class with no interface. Replace entirely:

```csharp
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace MSOSync.TestPlugin;

public sealed class TestPlugin : PluginBase
{
    // Static flags — reset between tests via Reset()
    public static bool InitializeCalled { get; private set; }
    public static bool StartCalled      { get; private set; }
    public static bool StopCalled       { get; private set; }
    public static bool DisposeCalled    { get; private set; }

    public static void Reset()
    {
        InitializeCalled = false;
        StartCalled      = false;
        StopCalled       = false;
        DisposeCalled    = false;
    }

    public override Task InitializeAsync(IPluginContext ctx, CancellationToken ct)
    {
        InitializeCalled = true;
        return base.InitializeAsync(ctx, ct);
    }

    public override Task StartAsync(CancellationToken ct)
    {
        StartCalled = true;
        return base.StartAsync(ct);
    }

    public override Task StopAsync(CancellationToken ct)
    {
        StopCalled = true;
        return base.StopAsync(ct);
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return base.DisposeAsync();
    }
}
```

- [ ] **Step 3: Build `MSOSync.TestPlugin` in Release configuration**

```powershell
dotnet build tests\MSOSync.TestPlugin\MSOSync.TestPlugin.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors. The DLL will be at `tests\MSOSync.TestPlugin\bin\Release\net9.0\MSOSync.TestPlugin.dll`.

- [ ] **Step 4: Copy the rebuilt DLL to TestAssets**

```powershell
Copy-Item `
  "tests\MSOSync.TestPlugin\bin\Release\net9.0\MSOSync.TestPlugin.dll" `
  "tests\MSOSync.IntegrationTests\TestAssets\plugins\test-plugin\MSOSync.TestPlugin.dll" `
  -Force
```

Do NOT copy `MSOSync.Sdk.dll` into the plugin folder. The plugin's `AssemblyLoadContext.Load()` returns `null` for `MSOSync.Sdk` (it's not in the plugin dir or lib/), which causes the runtime to fall back to the host's shared context where `MSOSync.Sdk.dll` is already loaded. This is correct behaviour.

- [ ] **Step 5: Update `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/plugin.json`**

Current content:

```json
{
  "id": "msosync.test",
  "name": "MSOSync Test Plugin",
  "version": "1.0.0",
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Minimal plugin for integration tests.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

Replace with:

```json
{
  "manifestVersion": 1,
  "id": "msosync.test",
  "name": "MSOSync Test Plugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Minimal plugin for integration tests.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

- [ ] **Step 6: Verify the DLL is different (not identical to pre-14B)**

```powershell
(Get-Item "tests\MSOSync.IntegrationTests\TestAssets\plugins\test-plugin\MSOSync.TestPlugin.dll").LastWriteTime
```

The timestamp should be the current time (just rebuilt), not an old timestamp.

- [ ] **Step 7: Build MSOSync.IntegrationTests to verify the project still compiles**

```powershell
dotnet build tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj
```

Expected: `Build succeeded.` The integration test project does not need code changes in this task — just verifying nothing broke.

- [ ] **Step 8: Commit**

```powershell
git add tests\MSOSync.TestPlugin\ tests\MSOSync.IntegrationTests\TestAssets\
git commit -m "feat(14B-3): TestPlugin implements IPlugin via PluginBase; plugin.json adds sdkVersion/apiVersion/manifestVersion/startupOrder"
```
