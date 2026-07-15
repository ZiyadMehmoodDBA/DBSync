# Epic 14B — Task 9: Integration Tests + Frontend Update

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Create `MSOSync.Plugin.IntegrationTests` (10 integration tests that exercise the real plugin runtime with the actual `TestPlugin.dll`), modify `TestPlugin` to expose captured config for config-layer tests, and update the frontend `PluginStatusBadge` + `types.ts` to reflect the new 6-value `PluginStatus` enum.

**Architecture:** Integration tests split into two harness styles: (1) a `PluginHostHarness` that spins up a minimal `IHost` with real plugin services for full end-to-end scenarios (lifecycle, config, health, ordering, SDK compat); (2) direct `PluginLifecycleManager` instantiation with mock `IPlugin` instances for controlled failure-injection scenarios (timeout, throws). The frontend changes are purely additive — new status values, icon prefixes.

**Tech Stack:** C# 13 / .NET 9 / `Microsoft.Extensions.Hosting` / xUnit + FluentAssertions + Moq / React 19 TypeScript

## Global Constraints

- `MSOSync.Sdk` must have zero NuGet dependencies and zero project references — it builds in complete isolation
- All projects: `net9.0`, `LangVersion 13.0`, `Nullable enable`, `TreatWarningsAsErrors true`
- Package versions managed centrally in `Directory.Packages.props` — no explicit versions in individual `.csproj` files
- `PluginStatus` enum values in 14B: `Loaded, Initialized, Running, Stopped, Disabled, Failed`
- `PluginRuntimeState` enum is `internal` — 11 values: `Loaded, Initializing, Initialized, Starting, Running, Stopping, Stopped, Disposing, Disposed, Failed, Disabled`
- `CompatibilityResult` enum values: `Compatible, Warning, Incompatible`
- `StatusVariant` in frontend: `'success' | 'warning' | 'danger' | 'neutral'` (no 'active' or 'error')
- `TestPlugin.dll` is NOT referenced as a `ProjectReference` — it is a pre-built binary in `TestAssets/`
- `PluginRegistry.GetRuntime(string)` and `GetAllRuntimes()` are internal methods (added Task 6) — available via `InternalsVisibleTo`

## Files

**Modify:**
- `src/MSOSync.Plugin/MSOSync.Plugin.csproj` — add `InternalsVisibleTo("MSOSync.Plugin.IntegrationTests")`
- `tests/MSOSync.TestPlugin/TestPlugin.cs` — add `CapturedTimeout` static property
- `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/plugin.json` — update `sdkVersion`, `apiVersion`, `startupOrder` (same change as Task 3)
- `src/MSOSync.Frontend/src/features/plugins/types.ts` — update `PluginStatus` union, extend `PluginDto`
- `src/MSOSync.Frontend/src/features/plugins/PluginStatusBadge.tsx` — 6-status icon+color mapping

**Create:**
- `tests/MSOSync.Plugin.IntegrationTests/MSOSync.Plugin.IntegrationTests.csproj`
- `tests/MSOSync.Plugin.IntegrationTests/PluginHostHarness.cs`
- `tests/MSOSync.Plugin.IntegrationTests/FullLifecycleTests.cs`
- `tests/MSOSync.Plugin.IntegrationTests/LifecycleFailureTests.cs`
- `tests/MSOSync.Plugin.IntegrationTests/PluginConfigIntegrationTests.cs`
- `tests/MSOSync.Plugin.IntegrationTests/HealthAndCompatTests.cs`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test/plugin.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test/plugin.config.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test.badconfig/plugin.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test.badconfig/plugin.config.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.order100/plugin.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.order200/plugin.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.order300/plugin.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.duptest-a/plugin.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.duptest-b/plugin.json`
- `tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.badsdk/plugin.json`

**Note on DLL files:** `TestAssets` plugin directories need `MSOSync.TestPlugin.dll`. These are **not source files** — copy the DLL built in Task 3 (`tests/MSOSync.TestPlugin/bin/Release/net9.0/MSOSync.TestPlugin.dll`) into each plugin directory after building TestPlugin. The `.csproj` uses `<None Include="TestAssets\**\*" CopyToOutputDirectory="PreserveNewest" />` to deploy them.

## Interfaces

**Consumes:**
- `PluginRegistry` (internal) + `.GetRuntime(string)` / `.GetAllRuntimes()` — Task 6
- `PluginRuntimeState` (internal enum) — Task 7
- `PluginRuntime` (internal sealed class, mutable properties) — Task 7
- `PluginLifecycleManager` (internal, constructor: `(PluginRegistry, IOptions<PluginHostOptions>, ILogger<PluginLifecycleManager>)`) — Task 7
- `IPluginRuntimeManager` interface + `PluginRuntimeManager` concrete — Task 8
- `PluginActivator` (internal) — Task 6
- `PluginStatus` (public enum) — Task 8
- `PluginHostOptions` (public, full set of timeout/size fields) — Task 8
- `PluginHealthCheck` — Task 8 (modified to use new PluginStatus)
- `TestPlugin.InitializeCalled / StartCalled / StopCalled / DisposeCalled / CapturedTimeout` — Task 3 + this task

**Produces:**
- 10 passing integration tests in `MSOSync.Plugin.IntegrationTests`
- Updated frontend `PluginStatus` type union: `'Loaded' | 'Initialized' | 'Running' | 'Stopped' | 'Disabled' | 'Failed'`
- Updated `PluginDto` with: `initializeDurationMs?`, `startDurationMs?`, `totalDurationMs?`, `initializedAt?`, `startedAt?`
- Updated `PluginStatusBadge` with 6-status icon+color mapping

---

- [ ] **Step 1: Add `InternalsVisibleTo` to `MSOSync.Plugin.csproj`**

Open `src/MSOSync.Plugin/MSOSync.Plugin.csproj` and add inside the first `<PropertyGroup>` (or a new `<ItemGroup>`):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="MSOSync.Plugin.IntegrationTests" />
</ItemGroup>
```

Note: `MSOSync.PluginTests` already has its own `InternalsVisibleTo` entry (added in Tasks 5–7). Add a second entry for the new project. Do not remove the existing one.

- [ ] **Step 2: Add `CapturedTimeout` to `TestPlugin.cs`**

The config integration tests need the plugin to capture a config value during `InitializeAsync`. Open `tests/MSOSync.TestPlugin/TestPlugin.cs` and add one static property + update `InitializeAsync`:

```csharp
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace MSOSync.TestPlugin;

public sealed class TestPlugin : PluginBase
{
    public static bool    InitializeCalled { get; private set; }
    public static bool    StartCalled      { get; private set; }
    public static bool    StopCalled       { get; private set; }
    public static bool    DisposeCalled    { get; private set; }
    public static string? CapturedTimeout  { get; private set; }

    public static void Reset()
    {
        InitializeCalled = false;
        StartCalled      = false;
        StopCalled       = false;
        DisposeCalled    = false;
        CapturedTimeout  = null;
    }

    public override Task InitializeAsync(IPluginContext ctx, CancellationToken ct)
    {
        InitializeCalled = true;
        CapturedTimeout  = ctx.Configuration.GetValue<string>("timeout");
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

- [ ] **Step 3: Build `MSOSync.TestPlugin` and copy DLL to all TestAssets plugin directories**

```powershell
dotnet build tests\MSOSync.TestPlugin\MSOSync.TestPlugin.csproj -c Release
```

Expected: `Build succeeded.` Copy `tests\MSOSync.TestPlugin\bin\Release\net9.0\MSOSync.TestPlugin.dll` to each plugin directory:

```powershell
$dll = "tests\MSOSync.TestPlugin\bin\Release\net9.0\MSOSync.TestPlugin.dll"
$dirs = @(
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.test",
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.test.badconfig",
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.order100",
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.order200",
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.order300",
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.duptest-a",
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.duptest-b",
    "tests\MSOSync.Plugin.IntegrationTests\TestAssets\plugins\msosync.badsdk"
)
foreach ($dir in $dirs) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item -Force $dll $dir
}
```

**Note:** `msosync.badsdk` needs the DLL to exist so the loader can open the file. The SDK compatibility validator rejects it before (or during) activation, so the type check does not matter.

- [ ] **Step 4: Create TestAssets `plugin.json` files**

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test/plugin.json`:
```json
{
  "id": "msosync.test",
  "name": "MSOSync Test Plugin",
  "version": "1.0.0",
  "manifestVersion": 1,
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

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test/plugin.config.json`:
```json
{
  "timeout": "10"
}
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test.badconfig/plugin.json`:
```json
{
  "id": "msosync.test.badconfig",
  "name": "MSOSync Bad Config Plugin",
  "version": "1.0.0",
  "manifestVersion": 1,
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Plugin with malformed config file.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.test.badconfig/plugin.config.json`:
```json
{ this is not valid json
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.order100/plugin.json`:
```json
{
  "id": "msosync.order100",
  "name": "Order 100 Plugin",
  "version": "1.0.0",
  "manifestVersion": 1,
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 100,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Startup order 100.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.order200/plugin.json`:
```json
{
  "id": "msosync.order200",
  "name": "Order 200 Plugin",
  "version": "1.0.0",
  "manifestVersion": 1,
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 200,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Startup order 200.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.order300/plugin.json`:
```json
{
  "id": "msosync.order300",
  "name": "Order 300 Plugin",
  "version": "1.0.0",
  "manifestVersion": 1,
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 300,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Startup order 300.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.duptest-a/plugin.json`:
```json
{
  "id": "msosync.duptest",
  "name": "Duplicate Plugin A",
  "version": "1.0.0",
  "manifestVersion": 1,
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 10,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "First instance of duplicate plugin id.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.duptest-b/plugin.json`:
```json
{
  "id": "msosync.duptest",
  "name": "Duplicate Plugin B",
  "version": "1.0.0",
  "manifestVersion": 1,
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 20,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Second instance of duplicate plugin id — should fail.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

`tests/MSOSync.Plugin.IntegrationTests/TestAssets/plugins/msosync.badsdk/plugin.json`:
```json
{
  "id": "msosync.badsdk",
  "name": "Bad SDK Version Plugin",
  "version": "1.0.0",
  "manifestVersion": 1,
  "sdkVersion": "2.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Plugin with incompatible SDK version.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

- [ ] **Step 5: Create `MSOSync.Plugin.IntegrationTests.csproj`**

```powershell
New-Item -ItemType Directory -Force "tests\MSOSync.Plugin.IntegrationTests" | Out-Null
```

Create `tests/MSOSync.Plugin.IntegrationTests/MSOSync.Plugin.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Plugin\MSOSync.Plugin.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="TestAssets\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Add the project to the solution:

```powershell
dotnet sln D:\MSOSync\MSOSync.sln add tests\MSOSync.Plugin.IntegrationTests\MSOSync.Plugin.IntegrationTests.csproj
```

Verify `Directory.Packages.props` already has `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Diagnostics.HealthChecks`. If missing, add them — but check first:

```powershell
Select-String "Microsoft.Extensions.Hosting" D:\MSOSync\Directory.Packages.props
```

- [ ] **Step 6: Create `PluginHostHarness.cs`**

This helper builds a minimal `IHost` with the full plugin runtime wired up — no DB, no HTTP, no auth. It mirrors the DI registrations from `Program.cs` as updated in Task 8.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Diagnostics;
using MSOSync.Plugin.Hosting;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;

namespace MSOSync.Plugin.IntegrationTests;

internal static class PluginHostHarness
{
    internal static string TestAssetPath(string subdir)
        => Path.Combine(
            Path.GetDirectoryName(typeof(PluginHostHarness).Assembly.Location)!,
            "TestAssets", "plugins", subdir);

    internal static async Task<IHost> StartAsync(
        string pluginsDir,
        Dictionary<string, string?>? extraConfig = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["PluginHost:PluginsPath"] = pluginsDir,
            ["PluginHost:HostVersion"] = "1.0.0",
        };
        if (extraConfig is not null)
            foreach (var (k, v) in extraConfig) inMemory[k] = v;

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(inMemory);
        builder.Logging.ClearProviders();   // suppress output noise in test runner

        var services = builder.Services;
        services.Configure<PluginHostOptions>(builder.Configuration.GetSection("PluginHost"));
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<IPluginRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<PluginActivator>();
        services.AddSingleton<PluginLifecycleManager>();
        services.AddSingleton<IPluginLoader, PluginLoader>();
        services.AddSingleton<PluginRuntimeManager>();
        services.AddSingleton<IPluginRuntimeManager>(sp => sp.GetRequiredService<PluginRuntimeManager>());
        services.AddSingleton<PluginHost>();
        services.AddSingleton<IPluginHost>(sp => sp.GetRequiredService<PluginHost>());
        services.AddHostedService(sp => sp.GetRequiredService<PluginHost>());
        services.AddHealthChecks().AddCheck<PluginHealthCheck>("plugins");

        configureServices?.Invoke(services);

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }
}
```

**Note:** If `PluginActivator` or `PluginLifecycleManager` live in a different namespace than shown (check Task 6/7 output), update the `using` directives accordingly.

- [ ] **Step 7: Create `FullLifecycleTests.cs`**

Tests: `FullLifecycle_ValidPlugin_ReachesRunning` and `StartupOrder_Ascending` and `DuplicatePluginId_FirstWins_SecondFails`.

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Plugin.Runtime;
using MSOSync.TestPlugin;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class FullLifecycleTests
{
    [Fact]
    public async Task FullLifecycle_ValidPlugin_ReachesRunning()
    {
        TestPlugin.Reset();
        await using var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.TestAssetPath("msosync.test"));

        var registry = host.Services.GetRequiredService<PluginRegistry>();
        var rt       = registry.GetRuntime("msosync.test");

        rt.Should().NotBeNull("msosync.test must be registered");
        rt!.State.Should().Be(PluginRuntimeState.Running);

        TestPlugin.InitializeCalled.Should().BeTrue();
        TestPlugin.StartCalled.Should().BeTrue();

        // timestamps populated
        rt.InitializedAt.Should().NotBeNull();
        rt.StartedAt.Should().NotBeNull();
        rt.InitializeDuration.Should().NotBeNull();
        rt.StartDuration.Should().NotBeNull();

        // public status via registry
        registry.GetById("msosync.test")!.Status.Should().Be(PluginStatus.Running);
    }

    [Fact]
    public async Task StartupOrder_Ascending()
    {
        // Three plugins with orders 200, 100, 300 in the same directory.
        // PluginLifecycleManager must call InitializeAsync in ascending order: 100 → 200 → 300.
        var order = new List<string>();

        await using var host = await PluginHostHarness.StartAsync(
            // Point at a parent dir that contains all three order dirs as subdirs.
            // The loader scans immediate subdirectories of PluginsPath.
            PluginHostHarness.TestAssetPath(string.Empty),   // → TestAssets/plugins/
            extraConfig: null,
            configureServices: services =>
            {
                // Intercept InitializeAsync calls to record order.
                // Because TestPlugin uses static fields, we can observe InitializeCalled but
                // not order. Use the PluginRegistry post-hoc to check descriptor.StartupOrder.
            });

        var registry = host.Services.GetRequiredService<PluginRegistry>();
        var runtimes = registry.GetAllRuntimes()
            .Where(r => r.Descriptor.PluginId.StartsWith("msosync.order"))
            .ToList();

        runtimes.Should().HaveCount(3);

        // All should have reached Running
        runtimes.Should().AllSatisfy(rt =>
            rt.State.Should().Be(PluginRuntimeState.Running));

        // InitializedAt timestamps must be in ascending order by startupOrder
        var byOrder = runtimes
            .OrderBy(r => r.Descriptor.StartupOrder)
            .ToList();

        byOrder[0].Descriptor.PluginId.Should().Be("msosync.order100");
        byOrder[1].Descriptor.PluginId.Should().Be("msosync.order200");
        byOrder[2].Descriptor.PluginId.Should().Be("msosync.order300");

        // InitializedAt should be non-decreasing (100 initialized before 200, etc.)
        byOrder[0].InitializedAt.Should().BeBefore(byOrder[1].InitializedAt!.Value.AddSeconds(1));
        byOrder[1].InitializedAt.Should().BeBefore(byOrder[2].InitializedAt!.Value.AddSeconds(1));
    }

    [Fact]
    public async Task DuplicatePluginId_FirstWins_SecondFails()
    {
        // Two subdirs both declare id = "msosync.duptest". First alphabetically wins.
        await using var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.TestAssetPath(string.Empty));

        var registry  = host.Services.GetRequiredService<PluginRegistry>();
        var allRuntimes = registry.GetAllRuntimes();

        // Exactly one runtime for "msosync.duptest" (dedup at registry level)
        var dupRuntimes = allRuntimes
            .Where(r => r.Descriptor.PluginId == "msosync.duptest")
            .ToList();

        dupRuntimes.Should().HaveCount(1, "duplicate plugin id should result in exactly one runtime");
        dupRuntimes[0].State.Should().Be(PluginRuntimeState.Running,
            "the first-discovered instance should succeed");
    }
}
```

**Note on `StartupOrder_Ascending`:** The `TestAssets/plugins/` parent dir contains ALL plugin subdirectories. This test is not isolated — other subdirs (msosync.test, msosync.badsdk, etc.) are also loaded. The test only checks the three `msosync.order*` plugins. If test isolation is required, create a separate `TestAssets/ordering/` subtree with only the three order dirs.

- [ ] **Step 8: Create `LifecycleFailureTests.cs`**

Tests: `InitializeAsync_Timeout_PluginFailed_OthersContinue`, `StartAsync_Throws_PluginFailed_OthersContinue`, `StopAsync_Throws_Logged_OthersStopped`.

These bypass the loader and activator — they inject mock `IPlugin` instances directly into the `PluginRegistry` and run `PluginLifecycleManager` methods. This requires `InternalsVisibleTo` (Step 1) and access to `PluginRuntime.Instance`.

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Plugin.Runtime;
using MSOSync.Sdk.Abstractions;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class LifecycleFailureTests
{
    // Build a PluginLifecycleManager with an empty PluginRegistry and given options.
    private static (PluginLifecycleManager lifecycle, PluginRegistry registry) BuildHarness(
        int defaultTimeoutSeconds = 30)
    {
        var opts     = new PluginHostOptions { DefaultTimeoutSeconds = defaultTimeoutSeconds };
        var registry = new PluginRegistry();
        var logger   = NullLogger<PluginLifecycleManager>.Instance;
        var lifecycle = new PluginLifecycleManager(registry, Options.Create(opts), logger);
        return (lifecycle, registry);
    }

    // Helper: add a pre-built runtime (with mock instance) directly to the registry.
    private static PluginRuntime AddRuntime(
        PluginRegistry registry,
        string pluginId,
        IPlugin plugin,
        int startupOrder = 1000,
        PluginRuntimeState state = PluginRuntimeState.Loaded)
    {
        var descriptor = new PluginDescriptor
        {
            PluginId     = pluginId,
            Name         = pluginId,
            Version      = "1.0.0",
            Status       = PluginStatus.Loaded,
            StartupOrder = startupOrder,
        };
        registry.Register(descriptor);
        var rt    = registry.GetRuntime(pluginId)!;
        rt.Instance = plugin;
        rt.State    = state;
        return rt;
    }

    [Fact]
    public async Task InitializeAsync_Timeout_PluginFailed_OthersContinue()
    {
        var (lifecycle, registry) = BuildHarness(defaultTimeoutSeconds: 1);

        // Slow plugin: hangs until cancelled
        var slowMock = new Mock<IPlugin>();
        slowMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns<IPluginContext, CancellationToken>(async (_, ct) =>
                await Task.Delay(TimeSpan.FromMinutes(10), ct));

        // Fast plugin: succeeds immediately
        var fastMock = new Mock<IPlugin>();
        fastMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var slowRt = AddRuntime(registry, "slow.plugin", slowMock.Object, startupOrder: 100);
        var fastRt = AddRuntime(registry, "fast.plugin", fastMock.Object, startupOrder: 200);

        await lifecycle.InitializeAllAsync(CancellationToken.None);

        slowRt.State.Should().Be(PluginRuntimeState.Failed, "slow plugin timed out");
        slowRt.LastException.Should().NotBeNull();

        fastRt.State.Should().Be(PluginRuntimeState.Initialized, "fast plugin should succeed despite slow one failing");
    }

    [Fact]
    public async Task StartAsync_Throws_PluginFailed_OthersContinue()
    {
        var (lifecycle, registry) = BuildHarness();

        // Throwing plugin: throws in StartAsync
        var throwingMock = new Mock<IPlugin>();
        throwingMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        throwingMock
            .Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Good plugin: all methods succeed
        var goodMock = new Mock<IPlugin>();
        goodMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        goodMock
            .Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var throwingRt = AddRuntime(registry, "throwing.plugin", throwingMock.Object, startupOrder: 100);
        var goodRt     = AddRuntime(registry, "good.plugin",     goodMock.Object,     startupOrder: 200);

        // Initialize both first
        await lifecycle.InitializeAllAsync(CancellationToken.None);
        throwingRt.State.Should().Be(PluginRuntimeState.Initialized);
        goodRt.State.Should().Be(PluginRuntimeState.Initialized);

        // Now start — throwing plugin fails, good plugin continues
        await lifecycle.StartAllAsync(CancellationToken.None);

        throwingRt.State.Should().Be(PluginRuntimeState.Failed);
        throwingRt.LastException!.Message.Should().Be("boom");
        goodRt.State.Should().Be(PluginRuntimeState.Running);
    }

    [Fact]
    public async Task StopAsync_Throws_Logged_OthersStopped()
    {
        var (lifecycle, registry) = BuildHarness();

        // Plugin that throws in StopAsync
        var throwingStop = new Mock<IPlugin>();
        throwingStop
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        throwingStop
            .Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        throwingStop
            .Setup(p => p.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("stop failed"));
        throwingStop
            .Setup(p => p.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        // Normal plugin
        var normalMock = new Mock<IPlugin>();
        normalMock.Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        normalMock.Setup(p => p.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        normalMock.Setup(p => p.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        normalMock.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var throwingRt = AddRuntime(registry, "throw.stop",  throwingStop.Object, startupOrder: 200);
        var normalRt   = AddRuntime(registry, "normal.stop", normalMock.Object,   startupOrder: 100);

        // Get both to Running
        await lifecycle.InitializeAllAsync(CancellationToken.None);
        await lifecycle.StartAllAsync(CancellationToken.None);

        throwingRt.State.Should().Be(PluginRuntimeState.Running);
        normalRt.State.Should().Be(PluginRuntimeState.Running);

        // Stop — the throwing plugin's exception must be swallowed; both stop
        // StopAllAsync must NOT throw even if one plugin does
        var act = async () => await lifecycle.StopAllAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // State is Stopped regardless of exception during StopAsync
        throwingRt.State.Should().Be(PluginRuntimeState.Stopped,
            "exceptions during StopAsync are swallowed; state still becomes Stopped");
        normalRt.State.Should().Be(PluginRuntimeState.Stopped);
    }
}
```

- [ ] **Step 9: Create `PluginConfigIntegrationTests.cs`**

Tests: `PluginConfig_AppsettingsWinsOverFile` and `PluginConfig_MalformedFile_NonFatal`.

```csharp
using FluentAssertions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Plugin.Runtime;
using MSOSync.TestPlugin;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class PluginConfigIntegrationTests
{
    [Fact]
    public async Task PluginConfig_AppsettingsWinsOverFile()
    {
        // plugin.config.json has timeout: "10"
        // appsettings has Plugins:msosync.test:timeout = "99"
        // IPluginConfiguration.GetValue<string>("timeout") must return "99"
        TestPlugin.Reset();

        await using var host = await PluginHostHarness.StartAsync(
            pluginsDir: PluginHostHarness.TestAssetPath("msosync.test"),
            extraConfig: new Dictionary<string, string?>
            {
                ["Plugins:msosync.test:timeout"] = "99"
            });

        TestPlugin.InitializeCalled.Should().BeTrue();
        TestPlugin.CapturedTimeout.Should().Be("99",
            "appsettings must win over plugin.config.json when same key is set in both");
    }

    [Fact]
    public async Task PluginConfig_MalformedFile_NonFatal()
    {
        // plugin.config.json is malformed JSON → non-fatal warning; plugin still activates
        // appsettings has timeout = "42" so CapturedTimeout must be "42"
        TestPlugin.Reset();

        await using var host = await PluginHostHarness.StartAsync(
            pluginsDir: PluginHostHarness.TestAssetPath("msosync.test.badconfig"),
            extraConfig: new Dictionary<string, string?>
            {
                ["Plugins:msosync.test.badconfig:timeout"] = "42"
            });

        var registry = host.Services.GetRequiredService<PluginRegistry>();
        var rt       = registry.GetRuntime("msosync.test.badconfig");

        rt.Should().NotBeNull();
        rt!.State.Should().Be(PluginRuntimeState.Running,
            "malformed plugin.config.json must not prevent activation");
        TestPlugin.CapturedTimeout.Should().Be("42");
    }
}
```

- [ ] **Step 10: Create `HealthAndCompatTests.cs`**

Tests: `SdkVersion_Mismatch_PluginFailed` and `Health_FailedPlugin_ReturnsDegraded`.

```csharp
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Plugin.Runtime;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class HealthAndCompatTests
{
    [Fact]
    public async Task SdkVersion_Mismatch_PluginFailed()
    {
        // plugin.json declares sdkVersion: "2.0" — host supports major version "1" only
        await using var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.TestAssetPath("msosync.badsdk"));

        var registry = host.Services.GetRequiredService<PluginRegistry>();
        var rt       = registry.GetRuntime("msosync.badsdk");

        rt.Should().NotBeNull();
        rt!.State.Should().Be(PluginRuntimeState.Failed,
            "SDK major version mismatch must result in Failed state");
        rt.LastException.Should().NotBeNull();
    }

    [Fact]
    public async Task Health_FailedPlugin_ReturnsDegraded()
    {
        // Use the badsdk plugin dir — it fails with SdkCompatibility → registry has Failed plugin
        // PluginHealthCheck must report Degraded when any plugin is in Failed status
        await using var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.TestAssetPath("msosync.badsdk"));

        var healthCheck = host.Services
            .GetRequiredService<IEnumerable<IHealthCheck>>()
            .OfType<PluginHealthCheck>()
            .Single();

        var ctx    = new HealthCheckContext { Registration = new HealthCheckRegistration("plugins", healthCheck, null, null) };
        var result = await healthCheck.CheckHealthAsync(ctx, CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded,
            "a Failed plugin must degrade the health check");
        result.Description.Should().Contain("msosync.badsdk");
    }
}
```

- [ ] **Step 11: Run new integration tests**

```powershell
dotnet test tests\MSOSync.Plugin.IntegrationTests -v minimal
```

Expected: **10 tests pass** (FullLifecycleTests: 3, LifecycleFailureTests: 3, PluginConfigIntegrationTests: 2, HealthAndCompatTests: 2).

Debug failures: most likely causes are namespace mismatches in the harness, missing `InternalsVisibleTo`, or PluginRuntime mutable property not yet set (check Task 7 output). Fix and re-run.

- [ ] **Step 12: Update `src/MSOSync.Frontend/src/features/plugins/types.ts`**

Replace the existing `PluginStatus` union and add new `PluginDto` fields:

```typescript
export type PluginStatus =
  | 'Loaded'
  | 'Initialized'
  | 'Running'
  | 'Stopped'
  | 'Disabled'
  | 'Failed';

export interface PluginDto {
  pluginId:              string;
  name:                  string;
  version:               string;
  status:                PluginStatus;
  loadDurationMs:        number;
  initializeDurationMs?: number;
  startDurationMs?:      number;
  totalDurationMs?:      number;
  loadedAt:              string;
  initializedAt?:        string;
  startedAt?:            string;
  lastError:             string | null;
  failureStage:          string | null;
  hostCompatibility:     string;
  capabilities:          string[];
  permissions:           string[];
  dependencies:          string[];
}

export interface PluginSummaryDto {
  total:             number;
  loaded:            number;
  failed:            number;
  disabled:          number;
  startupDurationMs: number;
  lastScanAt:        string | null;
}

export interface PluginManifestDto {
  id:             string;
  name:           string;
  version:        string;
  minHostVersion: string;
  maxHostVersion: string;
  entryAssembly:  string;
  entryType:      string;
  author:         string;
  description:    string;
  permissions:    string[];
  dependencies:   string[];
  capabilities:   string[];
}
```

- [ ] **Step 13: Update `src/MSOSync.Frontend/src/features/plugins/PluginStatusBadge.tsx`**

Replace the entire file with the 6-status mapping (icon + color). The `StatusVariant` available values are `'success' | 'warning' | 'danger' | 'neutral'` — map spec's 'active' → 'success', 'error' → 'danger'.

```typescript
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import type { StatusVariant } from '../../shared/utils/status';
import type { PluginStatus } from './types';

interface StatusConfig {
  variant: StatusVariant;
  icon:    string;
}

const STATUS_CONFIG: Record<PluginStatus, StatusConfig> = {
  Running:     { variant: 'success', icon: '✓' },
  Initialized: { variant: 'warning', icon: '⏳' },
  Loaded:      { variant: 'warning', icon: '⏳' },
  Stopped:     { variant: 'neutral', icon: '■' },
  Failed:      { variant: 'danger',  icon: '✕' },
  Disabled:    { variant: 'neutral', icon: '○' },
};

interface Props { status: PluginStatus }

export function PluginStatusBadge({ status }: Props) {
  const { variant, icon } = STATUS_CONFIG[status] ?? { variant: 'neutral', icon: '' };
  return <StatusBadge status={`${icon} ${status}`} variant={variant} />;
}
```

- [ ] **Step 14: TypeScript type check**

```powershell
cd src\MSOSync.Frontend; npx tsc --noEmit
```

Expected: no errors. If there are errors due to the removed `'Discovered'` and `'Validated'` types, search for all usages:

```powershell
Select-String -Path "src\MSOSync.Frontend\src\**\*.tsx","src\MSOSync.Frontend\src\**\*.ts" -Pattern "Discovered|Validated" -Recurse
```

Fix any remaining references — there should be none after this task, but check to be sure.

- [ ] **Step 15: Build full solution**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: `Build succeeded.` 0 errors, 0 warnings.

- [ ] **Step 16: Run all plugin tests**

```powershell
dotnet test tests\MSOSync.Plugin.IntegrationTests tests\MSOSync.PluginTests tests\MSOSync.SdkTests -v minimal
```

Expected: all tests pass.

- [ ] **Step 17: Commit**

```powershell
git add src\MSOSync.Plugin\MSOSync.Plugin.csproj
git add tests\MSOSync.TestPlugin\TestPlugin.cs
git add tests\MSOSync.Plugin.IntegrationTests\
git add src\MSOSync.Frontend\src\features\plugins\types.ts
git add src\MSOSync.Frontend\src\features\plugins\PluginStatusBadge.tsx
git commit -m "feat(14B-9): integration tests (10), TestPlugin config capture, frontend PluginStatus 6-value mapping"
```
