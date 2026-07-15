# Epic 14B — Task 4: Bridge Adapters

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement the four bridge adapters that translate host services to SDK interfaces, plus the concrete `PluginContext`. All are internal to `MSOSync.Plugin`. No unit tests in this task — the adapters are integration-tested in Tasks 8 and 9.

**Architecture:** Each adapter wraps a host-side service and presents the SDK-defined interface to the plugin. `PluginContext` aggregates all adapters into a single immutable context object passed to `InitializeAsync`. All types live in `MSOSync.Plugin/Runtime/`.

**Tech Stack:** C# 13 / .NET 9 / `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Global Constraints

- All types in this task are `internal sealed` — never exposed in the public API
- `TreatWarningsAsErrors=true` — handle nullable returns from `ILogger.BeginScope`
- `MSOSync.Plugin` already references `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`

## Files

**Create:**
- `src/MSOSync.Plugin/Runtime/PluginLoggerAdapter.cs`
- `src/MSOSync.Plugin/Runtime/PluginEnvironmentAdapter.cs`
- `src/MSOSync.Plugin/Runtime/PluginServicesAdapter.cs`
- `src/MSOSync.Plugin/Runtime/PluginContext.cs`

## Interfaces

**Consumes:**
- `IPluginLogger`, `IPluginEnvironment`, `IPluginServices`, `IPluginContext`, `PluginMetadata` (Task 1)
- `PluginHostOptions` (existing in `MSOSync.Plugin/Models/PluginHostOptions.cs` — will be extended in Task 8 but the existing fields `PluginsPath`, `HostVersion` are already there)

**Produces:**
- `PluginLoggerAdapter(ILogger logger)` — used by PluginActivator (Task 6)
- `PluginEnvironmentAdapter(IHostEnvironment, PluginHostOptions, string pluginDirectory)` — used by PluginActivator
- `PluginServicesAdapter(IServiceProvider provider)` — used by PluginActivator
- `PluginContext(PluginMetadata, IPluginLogger, IPluginConfiguration, IPluginServices, IPluginEnvironment)` — used by PluginActivator

---

- [ ] **Step 1: Create `src/MSOSync.Plugin/Runtime/PluginLoggerAdapter.cs`**

`ILogger.BeginScope<TState>()` returns `IDisposable?` (nullable). We must handle null.

```csharp
using Microsoft.Extensions.Logging;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginLoggerAdapter(ILogger logger) : IPluginLogger
{
    public void LogDebug(string message, params object?[] args)
        => logger.LogDebug(message, args);

    public void LogInformation(string message, params object?[] args)
        => logger.LogInformation(message, args);

    public void LogWarning(string message, params object?[] args)
        => logger.LogWarning(message, args);

    public void LogWarning(Exception exception, string message, params object?[] args)
        => logger.LogWarning(exception, message, args);

    public void LogError(Exception? exception, string message, params object?[] args)
        => logger.LogError(exception, message, args);

    public void LogCritical(Exception? exception, string message, params object?[] args)
        => logger.LogCritical(exception, message, args);

    public IDisposable BeginScope(string name)
        => logger.BeginScope(name) ?? NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
```

- [ ] **Step 2: Create `src/MSOSync.Plugin/Runtime/PluginEnvironmentAdapter.cs`**

`IHostEnvironment.IsDevelopment()` and `IsProduction()` are extension methods in `Microsoft.Extensions.Hosting`. The Plugin project already has `Microsoft.Extensions.Hosting.Abstractions` which provides these.

```csharp
using Microsoft.Extensions.Hosting;
using MSOSync.Plugin.Models;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginEnvironmentAdapter(
    IHostEnvironment   hostEnv,
    PluginHostOptions  options,
    string             pluginDirectory) : IPluginEnvironment
{
    public string EnvironmentName => hostEnv.EnvironmentName;
    public bool   IsDevelopment   => hostEnv.IsDevelopment();
    public bool   IsProduction    => hostEnv.IsProduction();
    public string HostVersion     => options.HostVersion;
    public string DataDirectory   => hostEnv.ContentRootPath;
    public string PluginDirectory => pluginDirectory;
}
```

- [ ] **Step 3: Create `src/MSOSync.Plugin/Runtime/PluginServicesAdapter.cs`**

The per-plugin sub-container (built in Task 6 `PluginActivator`) is wrapped here. `GetRequiredService` and `GetService` are extension methods from `Microsoft.Extensions.DependencyInjection`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginServicesAdapter(IServiceProvider provider) : IPluginServices
{
    public T GetRequiredService<T>() where T : notnull
        => provider.GetRequiredService<T>();

    public T? GetService<T>()
        => provider.GetService<T>();

    public IEnumerable<T> GetServices<T>()
        => provider.GetServices<T>();
}
```

- [ ] **Step 4: Create `src/MSOSync.Plugin/Runtime/PluginContext.cs`**

`IPluginContext` is immutable after construction. The same instance is passed to `InitializeAsync` and accessible via `PluginBase.Context` for the plugin's lifetime.

```csharp
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Metadata;

namespace MSOSync.Plugin.Runtime;

internal sealed class PluginContext(
    PluginMetadata       metadata,
    IPluginLogger        logger,
    IPluginConfiguration configuration,
    IPluginServices      services,
    IPluginEnvironment   environment) : IPluginContext
{
    public PluginMetadata       Metadata      { get; } = metadata;
    public IPluginLogger        Logger        { get; } = logger;
    public IPluginConfiguration Configuration { get; } = configuration;
    public IPluginServices      Services      { get; } = services;
    public IPluginEnvironment   Environment   { get; } = environment;
}
```

- [ ] **Step 5: Build MSOSync.Plugin to verify compilation**

```powershell
dotnet build src\MSOSync.Plugin\MSOSync.Plugin.csproj
```

Expected: `Build succeeded.` with 0 errors, 0 warnings.

- [ ] **Step 6: Commit**

```powershell
git add src\MSOSync.Plugin\Runtime\
git commit -m "feat(14B-4): bridge adapters — PluginLoggerAdapter, PluginEnvironmentAdapter, PluginServicesAdapter, PluginContext"
```
