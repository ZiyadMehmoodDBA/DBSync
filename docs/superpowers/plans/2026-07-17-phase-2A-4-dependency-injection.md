# Phase 2A.4 — Dependency Injection Lifetime Audit

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify no singleton captures scoped services. Document the two legitimate `IServiceProvider` injection sites with justification comments. Produce a DI lifetime reference table document.

**Architecture:** Audit found the DI registrations are correct — `WorkerStatusRegistry` was the only lifetime mismatch and was fixed in `fix: WorkerStatusRegistry uses IServiceScopeFactory` (commit `33f5db1`). Two `IServiceProvider` injection sites remain: `PluginServicesAdapter` (adapter pattern — legitimate) and `OperationService` (keyed service resolution — legitimate). This plan documents both with inline comments and produces the reference doc.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core DI / MediatR / Scrutor

## Global Constraints

- No new product features. Scope is strictly audit and documentation.
- Definition of Complete: audit scan passed + justification comments added + docs committed + `dotnet test` exits 0.
- RULE-DI-1: No singleton service holds a direct reference to a scoped service or `IServiceProvider` without documented justification.
- RULE-DI-2: `IServiceScopeFactory` is the correct pattern when a singleton needs scoped services.
- RULE-DI-3: `IServiceProvider` injection requires an inline comment explaining why `IServiceScopeFactory` is insufficient.

---

## File Map

**Modify:**
- `src/MSOSync.Plugin/Runtime/PluginServicesAdapter.cs` — add justification comment
- `src/MSOSync.Metadata/Operations/OperationService.cs` — add justification comment

**Create:**
- `docs/architecture/di-lifetime-reference.md`

---

## Task 1: Add Justification Comments to IServiceProvider Injection Sites

**Files:**
- Modify: `src/MSOSync.Plugin/Runtime/PluginServicesAdapter.cs`
- Modify: `src/MSOSync.Metadata/Operations/OperationService.cs`

- [ ] **Step 1: Read PluginServicesAdapter**

```
cat D:\MSOSync\src\MSOSync.Plugin\Runtime\PluginServicesAdapter.cs
```

Confirm the class is `internal sealed class PluginServicesAdapter(IServiceProvider provider) : IPluginServices` and uses `provider.GetRequiredService<T>()`, `provider.GetService<T>()`, `provider.GetServices<T>()`.

- [ ] **Step 2: Add justification comment to PluginServicesAdapter**

Add a single-line comment above the primary constructor parameter explaining why `IServiceProvider` is appropriate here:

```csharp
// Adapter pattern: transparently delegates all service resolution to the host container.
// IServiceScopeFactory is not used because this adapter does not own the scope —
// it surfaces the already-scoped container provided by the plugin host at call time.
internal sealed class PluginServicesAdapter(IServiceProvider provider) : IPluginServices
```

- [ ] **Step 3: Read OperationService**

```
cat D:\MSOSync\src\MSOSync.Metadata\Operations\OperationService.cs
```

Confirm the constructor signature and locate the two `GetKeyedService<IOperationHandler>(opType)` calls.

- [ ] **Step 4: Add justification comment to OperationService**

Add a single-line comment above the `IServiceProvider serviceProvider` constructor parameter:

```csharp
// Keyed service resolution: IOperationHandler implementations are registered with
// string keys matching operation type names. GetKeyedService<T>(key) is only
// available on IServiceProvider — IServiceScopeFactory does not expose it.
// OperationService is registered as scoped, so no lifetime mismatch exists.
IServiceProvider         serviceProvider) : IOperationService
```

- [ ] **Step 5: Build**

```powershell
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Plugin/Runtime/PluginServicesAdapter.cs
git add src/MSOSync.Metadata/Operations/OperationService.cs
git commit -m "docs(2A.4): add IServiceProvider justification comments"
```

---

## Task 2: Scan for DI Lifetime Violations and Write Reference Document

**Files:**
- Create: `docs/architecture/di-lifetime-reference.md`

- [ ] **Step 1: Scan for singleton registrations that hold IServiceProvider directly**

Run each scan and verify the output:

```powershell
# Find all AddSingleton registrations
grep -rn "AddSingleton" D:\MSOSync\src\ --include="*.cs" | grep -v "//"
```

```powershell
# Find classes with IServiceProvider in primary constructor
grep -rn "IServiceProvider" D:\MSOSync\src\ --include="*.cs" | grep -v "IServiceScopeFactory\|//\|using\|namespace\|interface\|\.GetRequiredService\|\.GetService\|\.GetKeyedService\|\.GetServices"
```

For each match from the second grep, verify:
- If the class is registered as singleton AND holds `IServiceProvider` directly, it is a violation unless it creates a scope per operation.
- `PluginServicesAdapter` — adapter pattern, scoped by plugin host — acceptable.
- `OperationService` — scoped registration, keyed service resolution — acceptable.

Expected result: No new violations found beyond the two documented sites.

- [ ] **Step 2: Create di-lifetime-reference.md**

Create `docs/architecture/di-lifetime-reference.md`:

```markdown
# DI Lifetime Reference

All services in MSOSync follow strict lifetime rules. Violations cause runtime
`InvalidOperationException` from the ASP.NET Core DI container.

## Lifetime Rules

| Lifetime | Can depend on | Cannot depend on |
|---|---|---|
| Singleton | Singleton | Scoped, Transient (via direct ctor) |
| Scoped | Singleton, Scoped | Transient (direct ctor — OK if transient is cheap) |
| Transient | Singleton, Scoped, Transient | — |

## When Singletons Need Scoped Services

Use `IServiceScopeFactory` and create a new async scope per operation:

```csharp
await using var scope = scopeFactory.CreateAsyncScope();
var service = scope.ServiceProvider.GetRequiredService<IScopedService>();
await service.DoWorkAsync();
```

This pattern is used in:
- `WorkerStatusRegistry` — publishes `WorkerStatusChangedEvent` via MediatR (which needs scoped `INotificationService`)

## Approved IServiceProvider Injection Sites

The following classes inject `IServiceProvider` directly. Each has an inline
justification comment explaining why `IServiceScopeFactory` is insufficient.

| Class | File | Justification |
|---|---|---|
| `PluginServicesAdapter` | `src/MSOSync.Plugin/Runtime/PluginServicesAdapter.cs` | Adapter pattern — surfaces the already-scoped container provided by plugin host. Does not own the scope. |
| `OperationService` | `src/MSOSync.Metadata/Operations/OperationService.cs` | Keyed service resolution via `GetKeyedService<IOperationHandler>(opType)`. `IServiceScopeFactory` does not expose keyed resolution. Service is registered as scoped — no lifetime mismatch. |

All other classes must use constructor injection with concrete service interfaces.
Adding a new `IServiceProvider` injection requires a PR comment documenting why.

## Key Registrations

| Service | Lifetime | Notes |
|---|---|---|
| `WorkerStatusRegistry` | Singleton | Uses `IServiceScopeFactory` for MediatR publish |
| `AppDbContext` | Scoped | EF Core — never inject into singletons |
| `IPublisher` (MediatR) | Transient | Resolved inside scoped context only |
| `IMemoryCache` | Singleton | Safe everywhere |
| `IOptions<T>` | Singleton | Safe everywhere |
| `ICurrentUserService` | Scoped | HTTP context — never inject into singletons |
| `IExportJobService` | Scoped | Depends on `AppDbContext` |
| `OperationService` | Scoped | Depends on `AppDbContext` + keyed `IOperationHandler` |
| `PluginServicesAdapter` | Transient | One per plugin call |

## Adding New Singletons

Checklist before registering a new singleton:
1. Does it inject `AppDbContext`? → Change to Scoped.
2. Does it inject `ICurrentUserService`? → Change to Scoped or inject `IHttpContextAccessor`.
3. Does it inject `IPublisher`? → Use `IServiceScopeFactory` and create a scope per publish.
4. Does it inject `IServiceProvider` directly? → Add inline comment; get PR review.
```

- [ ] **Step 3: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add docs/architecture/di-lifetime-reference.md
git commit -m "docs(2A.4): DI lifetime reference table and approved IServiceProvider sites"
```

---

## Completion Criteria

2A.4 is **Complete** when:
1. `grep -rn "IServiceProvider" src/ --include="*.cs" | grep -v "//\|using\|namespace\|interface\|ScopeFactory\|GetRequiredService\|GetService\|GetKeyedService"` returns only `PluginServicesAdapter` and `OperationService`.
2. Both files have inline justification comments above the `IServiceProvider` parameter.
3. `dotnet test` exits 0.
4. `docs/architecture/di-lifetime-reference.md` committed.
