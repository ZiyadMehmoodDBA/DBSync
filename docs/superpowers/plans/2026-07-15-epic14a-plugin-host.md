# Epic 14A: Plugin Host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a production-ready plugin host that discovers, validates, loads, and tracks plugins from a `plugins/` folder at startup using `AssemblyLoadContext` isolation, exposes an admin REST API, and renders a diagnostics UI at `/administration/plugins`.

**Architecture:** New `MSOSync.Plugin` project (depends only on `MSOSync.Common`) owns all abstractions, models, loading pipeline, registry, hosted service, and health check. `MSOSync.Persistence` implements `IPluginStore` via EF Core. `MSOSync.App` wires everything together. `MSOSync.Api` hosts `PluginController`. React frontend feature at `src/features/plugins/`.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / xUnit + FluentAssertions + Moq / React 19 + TanStack Query / Vite

## Global Constraints

- `MSOSync.Plugin` references ONLY `MSOSync.Common` — no EF Core, no ASP.NET, no `MSOSync.Persistence`
- All projects target `net9.0`, `LangVersion 13.0`, `Nullable enable` (from `Directory.Build.props`)
- Package versions managed in `Directory.Packages.props` — no explicit versions in `.csproj` files
- M029 migration adds `msosync.sync_plugin` table; total table count 42 → 43
- M029 also seeds `MANAGE_PLUGINS` permission (Admin-only) into `sync_permission` and `sync_role_permission`
- `PluginHost:PluginsPath` config key, default `Path.Combine(AppContext.BaseDirectory, "plugins")`
- Alphabetical one-pass dependency resolution — document the limitation in comments
- No plugin activation, no `IPlugin` interface, no DI registration of plugin types in 14A
- `IPluginRegistry.Register(PluginDescriptor)` — signature is fixed
- `PluginManifestValidator` and `PluginDependencyResolver` are static (no DI)
- Logging event IDs: `PluginHost1001`–`PluginHost1005` (see `PluginLogEvents`)
- Frontend route: `/administration/plugins`, ADMIN-only via `ManagePlugins` permission key
- All integration tests use `(localdb)\mssqllocaldb` and `WebApplicationFactory<Program>` pattern
- Schema = `Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync"`

---

## Tasks

| # | Task | File |
|---|------|------|
| 1 | Persistence layer — `SyncPlugin` entity, M029 migration, `IPluginStore`, `PluginStore` | [task-1](2026-07-15-epic14a-task-1-persistence.md) |
| 2 | Manifest models and validator — `PluginManifest`, `PluginManifestValidator`, `PluginLogEvents` | [task-2](2026-07-15-epic14a-task-2-manifest.md) |
| 3 | Load context and dependency resolver — `PluginLoadContext`, `IPluginRegistry` (interface), `PluginDependencyResolver` | [task-3](2026-07-15-epic14a-task-3-loadcontext.md) |
| 4 | Registry and loader — `PluginRuntime`, `PluginDescriptor`, `PluginLoadResult`, `PluginRegistry`, `IPluginLoader`, `PluginLoader` | [task-4](2026-07-15-epic14a-task-4-registry-loader.md) |
| 5 | Plugin host service — `PluginHostOptions`, `IPluginHost`, `PluginHost` (IHostedService) | [task-5](2026-07-15-epic14a-task-5-host-service.md) |
| 6 | Health check — `PluginHealthCheck` + health wiring in `Program.cs` | [task-6](2026-07-15-epic14a-task-6-health.md) |
| 7 | Controller and DI wiring — `PluginController`, `Program.cs` registrations, project refs | [task-7](2026-07-15-epic14a-task-7-controller.md) |
| 8 | Frontend — types, api, hooks, components, page, route, sidebar | [task-8](2026-07-15-epic14a-task-8-frontend.md) |
| 9 | Integration tests — test plugin DLL, `PluginsFixture`, `PluginControllerTests` | [task-9](2026-07-15-epic14a-task-9-integration-tests.md) |

---

## Key Interfaces Cross-Reference

```csharp
// MSOSync.Plugin/Abstractions/IPluginStore.cs
public interface IPluginStore
{
    Task<IReadOnlyList<PluginRecord>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PluginRecord record, CancellationToken ct);
    Task TouchAsync(string pluginId, CancellationToken ct);
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct);
}

// MSOSync.Plugin/Abstractions/IPluginRegistry.cs
public interface IPluginRegistry
{
    bool IsInitialized { get; }
    IReadOnlyList<PluginDescriptor> GetAll();
    PluginDescriptor? GetById(string pluginId);
    void Register(PluginDescriptor descriptor);
    void UpdateStatus(string pluginId, PluginStatus status, string? error = null);
    void MarkInitialized();
}

// MSOSync.Plugin/Abstractions/IPluginLoader.cs
public interface IPluginLoader
{
    IReadOnlyList<AssemblyLoadContext> LoadContexts { get; }
    Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(string pluginsPath, CancellationToken ct);
}
```

## Progress Ledger

SDD ledger: `.superpowers/sdd/progress-epic14a.md`
