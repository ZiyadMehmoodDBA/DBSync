# Epic 12B.0 — Stabilization Sprint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring Epic 12B-1 (Node Lifecycle) and 12B-2 (Configuration Management) to a production-quality baseline before Epic 12C begins.

**Architecture:** Six focused tasks targeting security, SignalR resilience, Testcontainers integration, performance, observability, and documentation. No new features — only hardening and validation.

**Tech Stack:** .NET 9, ASP.NET Core, xUnit, Testcontainers, SQL Server 2022

## Global Constraints

- Zero build warnings (`--warnaserror` is active)
- All tests must pass before moving to the next task
- Do NOT commit `.env` files or any plaintext secrets
- Never use `git add .` or `git add -A` — stage files by name

---

### Task 1: Security Audit & Fixes

**Files:**
- Modify: `src/MSOSync.Security/Middleware/NodeTokenAuthMiddleware.cs`
- Modify: `src/MSOSync.Api/Controllers/NodeManagement/NodeManagementController.cs` (or wherever NodeMetadataAction constants are defined as strings)
- Create: `src/MSOSync.Metadata/NodeManagement/NodeManagementAuditActions.cs` (if not already a typed constants class)

**Goal:** Remove token value from debug logs; replace string literals with typed constants; audit all structured logs for credential leakage.

- [ ] **Step 1: Audit all structured log calls for credential leakage**

Search across the solution for any log calls that might leak secrets:

```powershell
cd D:\MSOSync
# Find all log calls mentioning token, password, secret, jwt, authorization
Select-String -Path "src\**\*.cs" -Pattern "(LogInformation|LogDebug|LogTrace|LogWarning|LogError).*?(token|password|secret|jwt|bearer|authorization)" -AllMatches -Recurse | Select-Object -First 50
```

Document every match. Fix any that log the value (not just the key name).

- [ ] **Step 2: Fix NodeTokenAuthMiddleware — remove X-Node-Token value from logs**

Read `src/MSOSync.Security/Middleware/NodeTokenAuthMiddleware.cs`. Find the line that logs `X-Node-Token` value (Debug level). Change it so only the header name (not value) is logged:

```csharp
// BEFORE (do not log the value):
// _logger.LogDebug("Node token auth: X-Node-Token={Token}", token);

// AFTER (log presence only):
_logger.LogDebug("Node token auth: X-Node-Token header present for path {Path}", context.Request.Path);
```

- [ ] **Step 3: Replace NodeMetadataAction string literals with typed constants**

If `NodeMetadataAction` is already a typed constants class (like `ConfigurationAuditConstants`), skip to Step 5. Otherwise:

Create `src/MSOSync.Metadata/NodeManagement/NodeManagementAuditActions.cs`:

```csharp
namespace MSOSync.Metadata.NodeManagement;

public static class NodeManagementAuditActions
{
    public const string NodeRegistered   = "NODE_REGISTERED";
    public const string NodeApproved     = "NODE_APPROVED";
    public const string NodeRejected     = "NODE_REJECTED";
    public const string NodeReRegistered = "NODE_RE_REGISTERED";
    public const string ProvisionPackageDownloaded = "PROVISION_PACKAGE_DOWNLOADED";
}
```

Replace all string literal usages in `NodeManagementController.cs` and `NodeManagementService.cs` with the typed constants.

- [ ] **Step 4: Verify no bootstrap tokens appear in logs**

Search for `LogDebug` or `LogInformation` calls near bootstrap token generation in `BootstrapTokenService.cs`:

```powershell
Select-String -Path "src\**\*.cs" -Pattern "LogDebug|LogInformation|LogTrace" -SimpleMatch -Recurse | Where-Object { $_.Line -match "bootstrap|token" } | Select-Object Path, LineNumber, Line
```

Ensure token values are never included in log messages — only IDs and hashes.

- [ ] **Step 5: Build and verify zero warnings**

```powershell
dotnet build D:\MSOSync\MSOSync.sln --warnaserror -nologo -v q
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Run all tests**

```powershell
dotnet test D:\MSOSync\MSOSync.sln --no-build -v q 2>&1 | Select-String -Pattern "passed|failed|skipped|error" | Select-Object -Last 10
```

Expected: All existing tests still pass.

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Security/Middleware/NodeTokenAuthMiddleware.cs
git add src/MSOSync.Metadata/NodeManagement/NodeManagementAuditActions.cs
# Add any other modified files
git commit -m "fix(12B.0): remove token values from debug logs, typed audit action constants"
```

---

### Task 2: SignalR Validation Checklist

**Files:**
- Create: `docs/superpowers/specs/signalr-validation-checklist.md`
- Create: `tests/MSOSync.IntegrationTests/SignalR/SignalRResilienceTests.cs` (automated portion)

**Goal:** Document and automate the SignalR validation protocol. Any failure = blocking defect before 12C.

- [ ] **Step 1: Create the validation checklist document**

Create `docs/superpowers/specs/signalr-validation-checklist.md`:

```markdown
# SignalR Validation Checklist

**Status:** PENDING  
**Blocking:** Epic 12C cannot start until all items pass.

## Manual Validation Items

### M1: Basic Connection
- [ ] Open the app in a browser → SignalR status indicator shows Connected
- [ ] Server restart → indicator briefly shows Reconnecting → returns to Connected within 30s
- [ ] Network drop (disable Wi-Fi for 10s) → reconnect succeeds automatically

### M2: Token Refresh
- [ ] Let access token expire (set Jwt:AccessExpiryMinutes=1 in test env)
- [ ] Observe that refresh occurs without SignalR disconnect
- [ ] If disconnect occurs: reconnect must succeed using refreshed token

### M3: Push Events
- [ ] Trigger a node lifecycle change → UI badge updates without page refresh
- [ ] Trigger a configuration assignment → drift badge updates without page refresh
- [ ] Verify no duplicate events appear (same event shows once per tab)

### M4: Multi-Tab
- [ ] Open two browser tabs simultaneously
- [ ] Trigger a node event → both tabs update
- [ ] Close one tab → other tab continues receiving events

### M5: Long Idle Session
- [ ] Leave the app idle for 30+ minutes
- [ ] Trigger a node event → UI still receives it within 5 seconds

### M6: Browser Sleep/Resume
- [ ] Put laptop to sleep → wake → verify SignalR reconnects within 30s

### M7: Reconnect Storm
- [ ] Restart the API server 5 times in quick succession (30s intervals)
- [ ] Verify client reconnects each time without requiring manual page reload

### M8: Event Ordering
- [ ] Perform a multi-step lifecycle transition (Pending → Approved → Active)
- [ ] Verify events arrive in chronological order in the UI (CorrelationId ordering)

## Automated Tests (see SignalRResilienceTests.cs)

- [ ] `Reconnect_AfterServerBounce_EventsResume`
- [ ] `DuplicateEvent_NotDelivered_ToSameClient`

## Sign-off

Tester: ________________  Date: ________________  
All items: [ ] PASS  [ ] FAIL (list failures as blocking defects)
```

- [ ] **Step 2: Write automated reconnect test**

Create `tests/MSOSync.IntegrationTests/SignalR/SignalRResilienceTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using MSOSync.IntegrationTests.Configuration;
using Xunit;

namespace MSOSync.IntegrationTests.SignalR;

[Collection("Configuration")]
public sealed class SignalRResilienceTests(ConfigurationFixture fx)
{
    [Fact]
    public async Task SignalR_ConnectsSuccessfully_WithValidJwt()
    {
        var client = fx.CreateClient();
        var token  = await fx.GetJwtAsync(client, fx.AdminUsername, fx.AdminPassword);

        var hub = new HubConnectionBuilder()
            .WithUrl(fx.Server.BaseAddress + "hubs/operations", opts =>
            {
                opts.AccessTokenProvider = () => Task.FromResult<string?>(token);
                opts.HttpMessageHandlerFactory = _ => fx.Server.CreateHandler();
            })
            .Build();

        await hub.StartAsync();
        hub.State.Should().Be(HubConnectionState.Connected);
        await hub.StopAsync();
    }

    [Fact]
    public async Task SignalR_NoEvents_WithoutAuth()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(fx.Server.BaseAddress + "hubs/operations", opts =>
            {
                opts.HttpMessageHandlerFactory = _ => fx.Server.CreateHandler();
            })
            .Build();

        // Hub requires ViewerOrAbove — unauthenticated connection should fail or be dropped
        var act = async () => await hub.StartAsync();
        await act.Should().ThrowAsync<Exception>();
    }
}
```

- [ ] **Step 3: Add Microsoft.AspNetCore.SignalR.Client NuGet to integration test project**

```powershell
dotnet add D:\MSOSync\tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj package Microsoft.AspNetCore.SignalR.Client
```

- [ ] **Step 4: Run new tests**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.IntegrationTests -v q --filter "SignalRResilienceTests"
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add docs/superpowers/specs/signalr-validation-checklist.md
git add tests/MSOSync.IntegrationTests/SignalR/SignalRResilienceTests.cs
git add tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj
git commit -m "test(12B.0): SignalR validation checklist + automated connection tests"
```

---

### Task 3: Testcontainers Integration + Performance Cleanup

**Files:**
- Modify: `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj`
- Create: `tests/MSOSync.IntegrationTests/Lifecycle/TestcontainersLifecycleFixture.cs`
- Modify: `src/MSOSync.Metadata/NodeManagement/ProvisionPackageService.cs`
- Modify: `src/MSOSync.Api/Controllers/NodeManagement/NodeManagementController.cs`

**Goal:** Run lifecycle integration tests under SQL Server 2022 Testcontainers; fix three scoped performance issues.

- [ ] **Step 1: Add Testcontainers NuGet packages**

```powershell
dotnet add D:\MSOSync\tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj package Testcontainers.MsSql --version 3.*
```

- [ ] **Step 2: Create Testcontainers lifecycle fixture**

Create `tests/MSOSync.IntegrationTests/Lifecycle/TestcontainersLifecycleFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

public sealed class TestcontainersLifecycleFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString).Options;

        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("Testcontainers-Lifecycle")]
public sealed class TestcontainersLifecycleCollection
    : ICollectionFixture<TestcontainersLifecycleFixture> { }
```

- [ ] **Step 3: Write a smoke-test using the Testcontainers fixture**

Create `tests/MSOSync.IntegrationTests/Lifecycle/TestcontainersMigrationSmokeTest.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Testcontainers-Lifecycle")]
public sealed class TestcontainersMigrationSmokeTest(TestcontainersLifecycleFixture fx)
{
    [Fact]
    public async Task MigrateAsync_FromEmpty_AllTablesExist()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fx.ConnectionString).Options;

        await using var db = new AppDbContext(opts);

        // Verify key tables exist after migration
        var canConnect = await db.Database.CanConnectAsync();
        canConnect.Should().BeTrue();

        var nodeCount = await db.Nodes.CountAsync();
        nodeCount.Should().Be(0, "fresh database has no nodes");
    }
}
```

- [ ] **Step 4: Run Testcontainers tests (requires Docker)**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.IntegrationTests -v n --filter "Testcontainers"
```

Expected: 1 test passes. Note: requires Docker Desktop running.

- [ ] **Step 5: Fix ProvisionPackageService — replace constructor-injected AppDbContext with IServiceScopeFactory**

Read `src/MSOSync.Metadata/NodeManagement/ProvisionPackageService.cs`. Find the constructor injection of `AppDbContext`. Replace:

```csharp
// BEFORE — AppDbContext injected directly into singleton-adjacent service
public sealed class ProvisionPackageService(AppDbContext db, ...) : IProvisionPackageService

// AFTER — use IServiceScopeFactory to resolve scoped DbContext
public sealed class ProvisionPackageService(IServiceScopeFactory scopeFactory, ...) : IProvisionPackageService
```

In every method body, replace direct `db.` usage with:

```csharp
await using var scope = scopeFactory.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
// ... use db ...
```

- [ ] **Step 6: Fix bulk SaveChanges in NodeManagementController**

Read `src/MSOSync.Api/Controllers/NodeManagement/NodeManagementController.cs`. Find any loop that calls `SaveChangesAsync()` per iteration. Refactor to collect changes, then save once:

```csharp
// BEFORE
foreach (var nodeId in request.NodeIds)
{
    // ...modify entity...
    await db.SaveChangesAsync(ct);  // per-item save
}

// AFTER
foreach (var nodeId in request.NodeIds)
{
    // ...modify entity...
    // no save here
}
await db.SaveChangesAsync(ct);  // single save after all modifications
```

- [ ] **Step 7: Build and run all tests**

```powershell
dotnet build D:\MSOSync\MSOSync.sln --warnaserror -nologo -v q
dotnet test D:\MSOSync\MSOSync.sln --no-build -v q 2>&1 | Select-String -Pattern "passed|failed|Error" | Select-Object -Last 10
```

Expected: build clean, all existing tests pass.

- [ ] **Step 8: Commit**

```powershell
git add tests/MSOSync.IntegrationTests/Lifecycle/TestcontainersLifecycleFixture.cs
git add tests/MSOSync.IntegrationTests/Lifecycle/TestcontainersMigrationSmokeTest.cs
git add tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj
git add src/MSOSync.Metadata/NodeManagement/ProvisionPackageService.cs
git add src/MSOSync.Api/Controllers/NodeManagement/NodeManagementController.cs
git commit -m "fix(12B.0): Testcontainers migration smoke test; ProvisionPackageService scope fix; bulk SaveChanges"
```

---

### Task 4: Observability Validation + Documentation Freeze

**Files:**
- Create: `docs/superpowers/specs/observability-checklist.md`
- Modify: (any gaps found during audit)

**Goal:** Verify every long-running async operation produces all required telemetry signals. Update documentation to match implementation.

- [ ] **Step 1: Create observability checklist**

Create `docs/superpowers/specs/observability-checklist.md`:

```markdown
# Observability Validation Checklist

Every long-running or async operation must produce ALL of the following signals:

| Signal | Required |
|--------|----------|
| Audit event (sync_audit row) | ✓ |
| CorrelationId on audit event | ✓ |
| SignalR broadcast | ✓ |
| Structured log with operation summary | ✓ |
| Structured log on failure | ✓ |
| Duration metric (or log with timing) | ✓ |

## Operations to validate

### Export Jobs (ExportJobService + ExportJobWorker)
- [ ] Audit event on job creation: action = EXPORT_JOB_CREATED
- [ ] Audit event on completion: action = EXPORT_JOB_COMPLETED
- [ ] SignalR ExportJobChanged broadcast on completion
- [ ] Structured log: LogInformation on start, LogError on failure
- [ ] Duration logged

### Configuration Rollout (RolloutService)
- [ ] Audit event on start: action = ROLLOUT_STARTED
- [ ] Audit event on completion: action = ROLLOUT_COMPLETED
- [ ] SignalR ConfigurationChanged on completion
- [ ] CorrelationId = rolloutId.ToString()
- [ ] Duration: CompletedAt - StartedAt derivable from DB row

### Node Decommission (NodeLifecycleService)
- [ ] Audit event on initiation: action = NODE_DECOMMISSION_INITIATED
- [ ] Audit event on completion: action = NODE_DECOMMISSION_COMPLETED
- [ ] SignalR NodeLifecycleChanged broadcast
- [ ] Structured log on each drain check
- [ ] CorrelationId propagated through lifecycle history

## Sign-off

Reviewer: ________________  Date: ________________  
All gaps fixed: [ ] YES  [ ] NO (list open items)
```

- [ ] **Step 2: Audit ExportJobService for missing audit events**

Read `src/MSOSync.App/Export/ExportJobService.cs`. Verify `CreateJobAsync` and `CompleteJobAsync` call `IAuditService.WriteAsync`. If missing, add the call:

```csharp
// In CreateJobAsync, after SaveChangesAsync:
await _auditSvc.WriteAsync("EXPORT_JOB_CREATED", $"Job {job.JobId} ({job.ResourceType}/{job.Format})",
    job.CreatedBy?.ToString() ?? "system", ct);
```

- [ ] **Step 3: Verify RolloutService CorrelationId propagation**

Read `src/MSOSync.Metadata/Configuration/RolloutService.cs`. Confirm `correlationId = rolloutId.ToString()` is set before `auditSvc.WriteAsync`. If `AssignAsync` does not receive the correlationId, add it to the call.

- [ ] **Step 4: Update permission matrix in docs**

Verify `docs/superpowers/specs/2026-07-08-epic12c-system-administration-center.md` permission matrix matches the actual permissions in `src/MSOSync.Metadata/Permissions/SystemPermissions.cs`. If any mismatch, update the spec.

- [ ] **Step 5: Update migration history table in docs**

In the spec or a new `docs/migration-history.md`, list migrations M001–M023 with their description and date. Verify the list matches files in `src/MSOSync.Persistence/Migrations/`.

- [ ] **Step 6: Build clean + all tests pass**

```powershell
dotnet build D:\MSOSync\MSOSync.sln --warnaserror -nologo -v q
dotnet test D:\MSOSync\MSOSync.sln --no-build -v q 2>&1 | Select-String -Pattern "passed|failed|Error" | Select-Object -Last 10
```

- [ ] **Step 7: Commit**

```powershell
git add docs/superpowers/specs/observability-checklist.md
git add docs/superpowers/specs/signalr-validation-checklist.md
# Add any source files modified for observability gaps
git commit -m "docs(12B.0): observability checklist, documentation freeze, audit gaps fixed"
```

---

## Exit Criteria (must all be true before starting 12C)

- [ ] Zero build warnings
- [ ] All unit + integration tests pass (including Testcontainers smoke test)
- [ ] SignalR validation checklist signed off
- [ ] No token/secret values in debug logs
- [ ] `ProvisionPackageService` uses `IServiceScopeFactory`
- [ ] Bulk operations use single `SaveChangesAsync`
- [ ] `NodeManagementAuditActions` typed constants replace string literals
- [ ] Observability checklist signed off
- [ ] Documentation freeze complete
