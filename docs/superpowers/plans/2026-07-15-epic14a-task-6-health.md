# Epic 14A — Task 6: PluginHealthCheck

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement `PluginHealthCheck` (ASP.NET Core `IHealthCheck`), register it in `Program.cs` under the `"plugins"` tag, and unit-test all health states.

**Architecture:** `PluginHealthCheck` is registered as `AddCheck<PluginHealthCheck>("plugins")`. It queries `IPluginRegistry`. Health logic: registry not initialized → Unhealthy; any ENABLED plugin Failed → Degraded; otherwise Healthy. Disabled plugins are excluded.

**Tech Stack:** C# 13 / .NET 9 / `Microsoft.Extensions.Diagnostics.HealthChecks` / xUnit + FluentAssertions + Moq

## Global Constraints

- `PluginHealthCheck` lives in `MSOSync.Plugin` project (not MSOSync.App)
- `MSOSync.Plugin` must add `Microsoft.Extensions.Diagnostics.HealthChecks` package reference
- Disabled plugins (`Status == PluginStatus.Disabled`) are EXCLUDED from health evaluation

## Files

**Create:**
- `src/MSOSync.Plugin/Diagnostics/PluginHealthCheck.cs`
- `tests/MSOSync.PluginTests/Diagnostics/PluginHealthCheckTests.cs`

**Modify:**
- `src/MSOSync.Plugin/MSOSync.Plugin.csproj` — add `Microsoft.Extensions.Diagnostics.HealthChecks` package
- `src/MSOSync.App/Program.cs` — add `.AddCheck<PluginHealthCheck>("plugins")`

## Interfaces

**Consumes:**
- `IPluginRegistry.GetAll()`, `IPluginRegistry.IsInitialized` (Task 3/4)
- `PluginStatus` (Task 1)

**Produces:**
- `PluginHealthCheck` class (consumed by Task 7 DI wiring — already wired here in Program.cs)

---

- [ ] **Step 1: Add health checks package to `src/MSOSync.Plugin/MSOSync.Plugin.csproj`**

Add inside `<ItemGroup>`:

```xml
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
```

- [ ] **Step 2: Create `src/MSOSync.Plugin/Diagnostics/PluginHealthCheck.cs`**

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Diagnostics;

public sealed class PluginHealthCheck(IPluginRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!registry.IsInitialized)
            return Task.FromResult(HealthCheckResult.Unhealthy("Plugin host not yet started"));

        var enabledPlugins = registry.GetAll()
            .Where(p => p.Status != PluginStatus.Disabled)
            .ToList();

        if (enabledPlugins.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No enabled plugins"));

        var failed = enabledPlugins
            .Where(p => p.Status == PluginStatus.Failed)
            .ToList();

        if (failed.Count > 0)
        {
            var details = string.Join(", ", failed.Select(f => $"{f.PluginId} ({f.ErrorMessage})"));
            return Task.FromResult(HealthCheckResult.Degraded($"Failed plugins: {details}"));
        }

        return Task.FromResult(
            HealthCheckResult.Healthy($"{enabledPlugins.Count} plugin(s) loaded"));
    }
}
```

- [ ] **Step 3: Write failing tests**

`tests/MSOSync.PluginTests/Diagnostics/PluginHealthCheckTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Diagnostics;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Diagnostics;

public sealed class PluginHealthCheckTests
{
    private static PluginDescriptor MakePlugin(string id, PluginStatus status) => new()
    {
        PluginId = id, Name = id, Version = "1.0.0",
        Status   = status, LoadedAt = DateTime.UtcNow,
    };

    private static IPluginRegistry RegistryWith(bool initialized, params PluginDescriptor[] plugins)
    {
        var mock = new Mock<IPluginRegistry>();
        mock.Setup(r => r.IsInitialized).Returns(initialized);
        mock.Setup(r => r.GetAll()).Returns(plugins.ToList());
        return mock.Object;
    }

    private static HealthCheckContext FakeContext() =>
        new() { Registration = new HealthCheckRegistration("plugins", Mock.Of<IHealthCheck>(), null, null) };

    [Fact]
    public async Task CheckHealth_RegistryNotInitialized_ReturnsUnhealthy()
    {
        var check  = new PluginHealthCheck(RegistryWith(false));
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_NoEnabledPlugins_ReturnsHealthy()
    {
        var reg    = RegistryWith(true, MakePlugin("p", PluginStatus.Disabled));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_AllLoaded_ReturnsHealthy()
    {
        var reg   = RegistryWith(true,
            MakePlugin("a", PluginStatus.Loaded),
            MakePlugin("b", PluginStatus.Loaded));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_OneFailedPlugin_ReturnsDegraded()
    {
        var reg   = RegistryWith(true,
            MakePlugin("a", PluginStatus.Loaded),
            MakePlugin("b", PluginStatus.Failed));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("b");
    }

    [Fact]
    public async Task CheckHealth_DisabledExcludedFromDegraded()
    {
        var reg   = RegistryWith(true,
            MakePlugin("loaded", PluginStatus.Loaded),
            MakePlugin("disabled", PluginStatus.Disabled));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/MSOSync.PluginTests --filter "PluginHealthCheckTests" -v minimal
```

Expected: All 5 tests pass.

- [ ] **Step 5: Register in `src/MSOSync.App/Program.cs`**

After the existing `.AddCheck<WorkerHealthCheck>("workers")` line:

```csharp
.AddCheck<MSOSync.Plugin.Diagnostics.PluginHealthCheck>("plugins");
```

Note: `MSOSync.App` will reference `MSOSync.Plugin` — that project reference is added in Task 7.

- [ ] **Step 6: Commit**

```bash
git add src/MSOSync.Plugin/MSOSync.Plugin.csproj src/MSOSync.Plugin/Diagnostics/PluginHealthCheck.cs tests/MSOSync.PluginTests/Diagnostics/PluginHealthCheckTests.cs src/MSOSync.App/Program.cs
git commit -m "feat(14A-6): PluginHealthCheck with Unhealthy/Degraded/Healthy states"
```
