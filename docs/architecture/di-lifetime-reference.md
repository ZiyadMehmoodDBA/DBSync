# DI Lifetime Reference

All services in MSOSync follow strict lifetime rules. Violations cause runtime
`InvalidOperationException` from the ASP.NET Core DI container (scope validation)
or, worse, silently captured stale scoped instances.

## Lifetime Rules

| Lifetime | Can depend on | Cannot depend on |
|---|---|---|
| Singleton | Singleton | Scoped, Transient (via direct ctor) |
| Scoped | Singleton, Scoped | Transient (direct ctor — OK if transient is cheap) |
| Transient | Singleton, Scoped, Transient | — |

- **RULE-DI-1:** No singleton service holds a direct reference to a scoped
  service or `IServiceProvider` without documented justification.
- **RULE-DI-2:** `IServiceScopeFactory` is the correct pattern when a singleton
  needs scoped services.
- **RULE-DI-3:** `IServiceProvider` injection requires an inline comment
  explaining why `IServiceScopeFactory` is insufficient.

## When Singletons Need Scoped Services

Use `IServiceScopeFactory` and create a new async scope per operation:

```csharp
await using var scope = scopeFactory.CreateAsyncScope();
var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
await service.DoWorkAsync();
```

This pattern is used in:

- `WorkerStatusRegistry` (singleton) — publishes worker status events via
  MediatR, which requires scoped resolution
- All `BackgroundService` workers (e.g., `ExportJobWorker`, `SyncJob`,
  `PullJob`, `RetryJob`, `PurgeJob`, `HeartbeatWorker`, `ProbeWorker`) —
  hosted services are singletons; each tick creates a scope for
  `AppDbContext`-backed services

## Approved IServiceProvider Injection Sites

The following classes inject `IServiceProvider` directly. Each has an inline
justification comment explaining why `IServiceScopeFactory` is insufficient
(RULE-DI-3).

| Class | File | Justification |
|---|---|---|
| `PluginServicesAdapter` | `src/MSOSync.Plugin/Runtime/PluginServicesAdapter.cs` | Adapter pattern — surfaces the plugin child container's provider to plugins via `IPluginServices`. Does not own the scope; registered inside the plugin's own container (`PluginActivator`). |
| `OperationService` | `src/MSOSync.Metadata/Operations/OperationService.cs` | Keyed service resolution via `GetKeyedService<IOperationHandler>(opType)`. `IServiceScopeFactory` does not expose keyed resolution. Registered scoped — no lifetime mismatch. |

Passing a scope's `ServiceProvider` as a **method parameter** (e.g.,
`ExportJobWorker.WriteExportFileAsync(scope.ServiceProvider, …)`) is not
injection and is acceptable — the caller owns the scope.

All other classes must use constructor injection with concrete service
interfaces. Adding a new `IServiceProvider` injection requires an inline
justification comment and PR review.

## Key Registrations

| Service | Lifetime | Notes |
|---|---|---|
| `IWorkerStatusRegistry` / `WorkerStatusRegistry` | Singleton | Uses `IServiceScopeFactory` for MediatR publish |
| `AppDbContext` | Scoped | EF Core — never inject into singletons |
| `IPublisher` (MediatR) | Scoped resolution | Resolve inside a scope only |
| `IOptions<T>` / `IOptionsMonitor<T>` | Singleton | Safe everywhere |
| `ICurrentUserService` | Scoped | HTTP context — never inject into singletons |
| `ICurrentTenantAccessor` | Singleton | Reads `IHttpContextAccessor` at call time — safe |
| `IExportJobService` | Scoped | Depends on `AppDbContext` |
| `IOperationService` / `OperationService` | Scoped | Keyed `IOperationHandler` resolution (all handlers `AddKeyedScoped`) |
| `IPluginServices` / `PluginServicesAdapter` | Singleton (plugin child container) | Wraps that container's provider; registered per plugin in `PluginActivator` |
| `PluginHost`, `PluginRegistry`, `PluginActivator` | Singleton | Plugin infrastructure — host-level state |
| `ISystemHealthService` + contributors | Singleton | Poll-based, no scoped deps in ctor |
| `NodeLifecycleLockRegistry`, `INodeLifecycleStateMachine` | Singleton | Pure in-memory state / stateless policy |
| `JwtService`, `BCryptPasswordHasher`, `PasswordPolicy`, `AuthMetrics` | Singleton | Stateless / config-driven |
| Metadata query & domain services (`I*QueryService`, `I*MetadataService`, `IPermissionService`, …) | Scoped | Depend on `AppDbContext` |
| FluentValidation validators | Scoped | Registered per filter/request type |

## Adding New Singletons

Checklist before registering a new singleton:

1. Does it inject `AppDbContext`? → Change to Scoped.
2. Does it inject `ICurrentUserService`? → Change to Scoped or inject `IHttpContextAccessor`.
3. Does it inject `IPublisher`? → Use `IServiceScopeFactory` and create a scope per publish.
4. Does it inject `IServiceProvider` directly? → Add inline justification comment (RULE-DI-3); get PR review.
