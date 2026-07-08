# Epic 12C — Task 5: Domain Integration (Wire IOperationService into ExportJobService, RolloutService, NodeLifecycleService)

**Branch:** `feat/epic12c-system-admin`  
**Files touched:** 3 modified (services), 1 modified (interface), 1 modified (handler stub), 1 new (integration tests)  
**Depends on:** Task 3 complete (`IOperationService` exists and is DI-registered).

---

## Context

This task wires `IOperationService` into the three domain services that own long-running jobs. Each service must:

1. Call `IOperationService.CreateAsync` **before** returning to its caller — not in the background — so the caller can include the `operationId` in its response.
2. Call `IOperationService.CompleteAsync` when the job succeeds (pass `OperationResult.Success`) or fails (pass `OperationResult.Failure`) — this may happen inside a background `Task.Run` lambda.
3. Call `IOperationService.UpdateProgressAsync` at meaningful progress milestones.

The `DecommissionOperationHandler.CancelAsync` stub (created in Task 3) is completed here by adding `CancelDecommissionAsync` to `INodeLifecycleService` and implementing it in `NodeLifecycleService`.

---

## Steps

### A. ExportJobService — operation tracking

- [ ] **A1. Add `IOperationService` to ExportJobService**

  Open `src/MSOSync.App/Export/ExportJobService.cs`.

  Change the primary constructor from:

  ```csharp
  public sealed class ExportJobService(
      AppDbContext db,
      IMediator mediator,
      IOptions<ExportOptions> opts)
      : IExportJobService
  ```

  to:

  ```csharp
  using MSOSync.Metadata.Operations;   // add this using at the top of the file

  public sealed class ExportJobService(
      AppDbContext db,
      IMediator mediator,
      IOptions<ExportOptions> opts,
      IOperationService operationService)
      : IExportJobService
  ```

  Add a private field to hold the operation ID (keyed by job ID) across calls. Because `ExportJobService` is `Scoped`, the simplest approach is a `Dictionary<Guid, Guid>` instance field:

  ```csharp
  // Tracks JobId → OperationId for this service scope (one request / one background task).
  private readonly Dictionary<Guid, Guid> _operationIds = new();
  ```

- [ ] **A2. Call CreateAsync in `CreateJobAsync`**

  In `CreateJobAsync`, after `await db.SaveChangesAsync(ct)` and the `s_created.Add(1)` counter, add:

  ```csharp
  var operationId = await operationService.CreateAsync(
      type:          OperationType.Export,
      referenceId:   job.JobId,
      initiatedBy:   null,   // export jobs are keyed by username string, not Guid
      source:        OperationSource.User,
      correlationId: job.JobId.ToString(),
      canCancel:     true,
      canRetry:      false,
      summary:       $"Export {job.ResourceType} to {job.Format}",
      metadataJson:  $"{{\"format\":\"{job.Format}\",\"resourceType\":\"{job.ResourceType}\"}}",
      ct:            ct);

  _operationIds[job.JobId] = operationId;
  ```

  The full updated method looks like:

  ```csharp
  public async Task<SyncExportJob> CreateJobAsync(
      string requestedBy, string resourceType, string format,
      string filtersJson, Guid? parentJobId, CancellationToken ct)
  {
      var job = new SyncExportJob
      {
          JobId        = Guid.NewGuid(),
          ParentJobId  = parentJobId,
          RequestedBy  = requestedBy,
          ResourceType = resourceType,
          Format       = format,
          FiltersJson  = filtersJson,
          Status       = ExportJobStatus.Pending,
          CreatedAt    = DateTimeOffset.UtcNow,
      };
      db.ExportJobs.Add(job);
      await db.SaveChangesAsync(ct);
      s_created.Add(1);

      var operationId = await operationService.CreateAsync(
          type:          OperationType.Export,
          referenceId:   job.JobId,
          initiatedBy:   null,
          source:        OperationSource.User,
          correlationId: job.JobId.ToString(),
          canCancel:     true,
          canRetry:      false,
          summary:       $"Export {job.ResourceType} to {job.Format}",
          metadataJson:  $"{{\"format\":\"{job.Format}\",\"resourceType\":\"{job.ResourceType}\"}}",
          ct:            ct);

      _operationIds[job.JobId] = operationId;

      return job;
  }
  ```

- [ ] **A3. Call UpdateProgressAsync in `UpdateProgressAsync`**

  In the existing `UpdateProgressAsync` method, after the `ExecuteUpdateAsync` call, add:

  ```csharp
  if (_operationIds.TryGetValue(jobId, out var opId))
      await operationService.UpdateProgressAsync(opId, progressPercent, null, ct);
  ```

  > **Note:** When the export worker picks up a job via `ClaimNextPendingJobAsync`, the `ExportJobService` instance is from a new DI scope and `_operationIds` is empty. To bridge the gap, look up the operation ID from the database when the dictionary misses. Add a private helper:
  >
  > ```csharp
  > private async Task<Guid?> FindOperationIdAsync(Guid jobId, CancellationToken ct)
  > {
  >     if (_operationIds.TryGetValue(jobId, out var cached)) return cached;
  >     var op = await db.Operations.AsNoTracking()
  >         .Where(o => o.ReferenceId == jobId && o.OperationType == "Export")
  >         .OrderByDescending(o => o.StartedAt)
  >         .FirstOrDefaultAsync(ct);
  >     return op?.OperationId;
  > }
  > ```
  >
  > Then replace the `_operationIds.TryGetValue` calls with `await FindOperationIdAsync(...)`:
  >
  > ```csharp
  > var opId = await FindOperationIdAsync(jobId, ct);
  > if (opId.HasValue)
  >     await operationService.UpdateProgressAsync(opId.Value, progressPercent, null, ct);
  > ```

- [ ] **A4. Call CompleteAsync in `CompleteJobAsync` and `FailJobAsync`**

  In `CompleteJobAsync`, after `await PublishAsync(jobId, ct)`:

  ```csharp
  var opId = await FindOperationIdAsync(jobId, ct);
  if (opId.HasValue)
      await operationService.CompleteAsync(opId.Value, OperationResult.Success,
          $"Exported {rowCount} rows to {Path.GetFileName(outputPath)}", ct);
  ```

  In `FailJobAsync`, after `await PublishAsync(jobId, ct)`:

  ```csharp
  var opId = await FindOperationIdAsync(jobId, ct);
  if (opId.HasValue)
      await operationService.CompleteAsync(opId.Value, OperationResult.Failure, errorMessage, ct);
  ```

---

### B. RolloutService — operation tracking

- [ ] **B1. Add `IOperationService` to RolloutService**

  Open `src/MSOSync.Metadata/Configuration/RolloutService.cs`.

  Change the primary constructor from:

  ```csharp
  public sealed class RolloutService(
      AppDbContext db,
      IAuditService auditSvc,
      IServiceScopeFactory scopeFactory) : IRolloutService
  ```

  to:

  ```csharp
  using MSOSync.Metadata.Operations;   // add at the top

  public sealed class RolloutService(
      AppDbContext db,
      IAuditService auditSvc,
      IServiceScopeFactory scopeFactory,
      IOperationService operationService) : IRolloutService
  ```

- [ ] **B2. Call CreateAsync in `StartRolloutAsync`**

  In `StartRolloutAsync`, after the `await auditSvc.WriteAsync(...)` call (line ~38 in the current file) and before the `_ = Task.Run(...)` block, add:

  ```csharp
  var operationId = await operationService.CreateAsync(
      type:          OperationType.Rollout,
      referenceId:   rolloutId,
      initiatedBy:   actorId,
      source:        OperationSource.User,
      correlationId: correlationId,
      canCancel:     true,
      canRetry:      false,
      summary:       $"Rollout template {templateId} v{version} to {nodeIds.Count} node(s)",
      metadataJson:  $"{{\"templateId\":\"{templateId}\",\"targetNodeCount\":{nodeIds.Count}}}",
      ct:            ct);
  ```

- [ ] **B3. Report progress and completion inside the background Task.Run**

  The background lambda in `StartRolloutAsync` uses `bgDb` and `bgAssignmentSvc`. We must capture `operationId` and resolve a fresh `IOperationService` from the child scope. Update the `_ = Task.Run(...)` block as follows:

  ```csharp
  var capturedOperationId = operationId;   // add alongside other captured vars

  _ = Task.Run(async () =>
  {
      await using var scope     = scopeFactory.CreateAsyncScope();
      var bgDb                  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var bgAssignmentSvc       = scope.ServiceProvider.GetRequiredService<IConfigurationAssignmentService>();
      var bgOpSvc               = scope.ServiceProvider.GetRequiredService<IOperationService>();

      int succeeded = 0, failed = 0;
      foreach (var nodeId in capturedNodeIds)
      {
          try
          {
              await bgAssignmentSvc.AssignAsync(nodeId, capturedTemplateId, capturedVersion,
                  capturedActor, capturedCorrelation, CancellationToken.None);
              succeeded++;
          }
          catch (OperationCanceledException)
          {
              throw;
          }
          catch
          {
              failed++;
          }

          var bgRollout = await bgDb.ConfigurationRollouts.FindAsync(capturedRolloutId);
          if (bgRollout is not null)
          {
              // Check if cancelled by operator (RolloutOperationHandler.CancelAsync)
              if (bgRollout.Status == "Cancelled")
              {
                  await bgOpSvc.CompleteAsync(capturedOperationId, OperationResult.Cancelled,
                      "Rollout cancelled by operator", CancellationToken.None);
                  return;
              }

              bgRollout.AppliedCount    = succeeded;
              bgRollout.FailedCount     = failed;
              bgRollout.PendingCount    = capturedTotal - succeeded - failed;
              bgRollout.ProgressPercent = (succeeded + failed) * 100 / capturedTotal;
              await bgDb.SaveChangesAsync(CancellationToken.None);

              await bgOpSvc.UpdateProgressAsync(
                  capturedOperationId,
                  bgRollout.ProgressPercent ?? 0,
                  $"Applied {succeeded}/{capturedTotal}, failed {failed}",
                  CancellationToken.None);
          }
      }

      var finalRollout = await bgDb.ConfigurationRollouts.FindAsync(capturedRolloutId);
      if (finalRollout is not null)
      {
          finalRollout.Status      = "Completed";
          finalRollout.CompletedAt = DateTime.UtcNow;
          await bgDb.SaveChangesAsync(CancellationToken.None);
      }

      var finalResult = failed == 0
          ? OperationResult.Success
          : (succeeded > 0 ? OperationResult.PartialSuccess : OperationResult.Failure);

      await bgOpSvc.CompleteAsync(capturedOperationId, finalResult,
          $"Rollout complete: {succeeded} succeeded, {failed} failed", CancellationToken.None);

  }, CancellationToken.None);
  ```

---

### C. NodeLifecycleService — decommission operation tracking

- [ ] **C1. Add `CancelDecommissionAsync` to INodeLifecycleService**

  Open `src/MSOSync.Metadata/NodeManagement/INodeLifecycleService.cs` and add one method at the end of the interface:

  ```csharp
  // ── 12C — Decommission cancellation (cancel-able within grace period) ─────────
  Task CancelDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default);
  ```

- [ ] **C2. Implement `CancelDecommissionAsync` in NodeLifecycleService**

  Open `src/MSOSync.Metadata/NodeManagement/NodeLifecycleService.cs`. Add the following method at the end of the class (after `FinalizeDecommissionAsync`):

  ```csharp
  public Task CancelDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default)
  {
      // Transition: Decommissioning → Disabled.
      // This is the only safe cancellation target — it is always reachable from Decommissioning
      // (the state machine allows Decommissioning → Disabled via a manual override trigger).
      // The node security was revoked at drain start (spec §4.7); it stays revoked here
      // because the node must re-authenticate via a new bootstrap token before being re-used.
      return ExecuteTransitionAsync(
          nodeId,
          NodeLifecycleState.Disabled,
          LifecycleTrigger.Manual,
          actorUsername,
          "Decommission cancelled by operator",
          NodeManagementAuditActions.NodeDecommissionCancelled,
          mutate: (node, _) =>
          {
              node.DecommissionReason        = null;
              node.DecommissionStartedAt     = null;
              node.DecommissionGraceUntil    = null;
              node.DecommissionInitialOpenBatches = null;
              return Task.CompletedTask;
          },
          ct: ct);
  }
  ```

  > **Audit action constant:** `NodeManagementAuditActions.NodeDecommissionCancelled` may not exist yet. Open `src/MSOSync.Metadata/NodeManagement/NodeManagementAuditActions.cs` (or wherever that static class lives) and add:
  > ```csharp
  > public const string NodeDecommissionCancelled = "node.decommission.cancelled";
  > ```

- [ ] **C3. Add `IOperationService` to NodeLifecycleService**

  Change the primary constructor. The current constructor has 12 parameters; add `IOperationService operationService` as the last one:

  ```csharp
  using MSOSync.Metadata.Operations;   // add at the top

  public sealed class NodeLifecycleService(
      AppDbContext                  db,
      IRegistrationDiffService      diffSvc,
      IAuditService                 auditSvc,
      IMediator                     mediator,
      INodeLifecycleStateMachine    stateMachine,
      INodeLifecycleHistoryService  history,
      IBootstrapTokenService        bootstrapTokens,
      NodeSecurityService           nodeSecurity,
      NodeLifecycleLockRegistry     locks,
      IOptions<LifecycleOptions>    options,
      IConfiguration                configuration,
      ILogger<NodeLifecycleService> logger,
      IOperationService             operationService) : INodeLifecycleService
  ```

- [ ] **C4. Call CreateAsync in `DecommissionAsync`**

  In `DecommissionAsync`, after the `await ExecuteTransitionAsync(...)` call completes, add:

  ```csharp
  // Track this decommission as an Operation so operators can monitor/cancel it.
  // referenceId is set to null here because NodeId is a string, not a Guid.
  // The node is identified via correlationId (the transition's correlationId stored in metadata).
  await operationService.CreateAsync(
      type:          OperationType.Decommission,
      referenceId:   null,
      initiatedBy:   Guid.TryParse(actorUsername, out var actorGuid) ? actorGuid : (Guid?)null,
      source:        OperationSource.User,
      correlationId: nodeId,   // use nodeId as the correlation to JOIN with lifecycle history
      canCancel:     true,
      canRetry:      false,
      summary:       $"Decommission node {nodeId}",
      metadataJson:  $"{{\"nodeId\":\"{nodeId}\",\"graceMinutes\":{gracePeriodMinutes ?? options.Value.DecommissionGraceMinutes}}}",
      ct:            ct);
  ```

  > **Important sequencing note:** `ExecuteTransitionAsync` is called first and awaited. Only after the transition commits successfully do we call `CreateAsync`. This ensures that if the transition fails (e.g. state-machine validation error), no orphan operation row is created.

- [ ] **C5. Complete the decommission operation when finalized**

  In `FinalizeDecommissionAsync` (which calls `ExecuteTransitionAsync` to `Decommissioned`), add after the `ExecuteTransitionAsync` call:

  ```csharp
  // Close the operation row that was opened in DecommissionAsync.
  // The operation is identified by correlationId = nodeId.
  var op = await db.Operations.AsNoTracking()
      .Where(o => o.CorrelationId == nodeId
               && o.OperationType == OperationType.Decommission.ToString()
               && o.Status != "Completed"
               && o.Status != "Cancelled"
               && o.Status != "Failed")
      .OrderByDescending(o => o.StartedAt)
      .FirstOrDefaultAsync(ct);

  if (op is not null)
      await operationService.CompleteAsync(op.OperationId, OperationResult.Success,
          $"Node {nodeId} fully decommissioned", ct);
  ```

  Similarly update `ForceCompleteDecommissionAsync`:

  ```csharp
  // After the base ExecuteTransitionAsync call:
  var op2 = await db.Operations.AsNoTracking()
      .Where(o => o.CorrelationId == nodeId
               && o.OperationType == OperationType.Decommission.ToString()
               && o.Status != "Completed"
               && o.Status != "Cancelled"
               && o.Status != "Failed")
      .OrderByDescending(o => o.StartedAt)
      .FirstOrDefaultAsync(ct);

  if (op2 is not null)
      await operationService.CompleteAsync(op2.OperationId, OperationResult.Success,
          $"Node {nodeId} decommissioned (forced by operator)", ct);
  ```

  > **Note:** `FinalizeDecommissionAsync` and `ForceCompleteDecommissionAsync` in `NodeLifecycleService` are currently one-liner `=>` expressions that return the result of `ExecuteTransitionAsync`. They need to be converted to block-body methods to add the operation-close code. See the exact code transformation in step C5a below.

- [ ] **C5a. Convert one-liner methods to block bodies**

  Replace:

  ```csharp
  public Task ForceCompleteDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default)
      => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, LifecycleTrigger.Manual,
          actorUsername, "forced by operator", NodeManagementAuditActions.NodeDecommissionForced,
          mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);

  public Task FinalizeDecommissionAsync(string nodeId, LifecycleTrigger trigger, string reason, CancellationToken ct = default)
      => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, trigger,
          "system", reason, NodeManagementAuditActions.NodeDecommissionCompleted,
          mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);
  ```

  With:

  ```csharp
  public async Task ForceCompleteDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default)
  {
      await ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, LifecycleTrigger.Manual,
          actorUsername, "forced by operator", NodeManagementAuditActions.NodeDecommissionForced,
          mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);

      var op = await db.Operations.AsNoTracking()
          .Where(o => o.CorrelationId == nodeId
                   && o.OperationType == "Decommission"
                   && o.Status != "Completed" && o.Status != "Cancelled" && o.Status != "Failed")
          .OrderByDescending(o => o.StartedAt)
          .FirstOrDefaultAsync(ct);

      if (op is not null)
          await operationService.CompleteAsync(op.OperationId, OperationResult.Success,
              $"Node {nodeId} decommissioned (forced by operator)", ct);
  }

  public async Task FinalizeDecommissionAsync(string nodeId, LifecycleTrigger trigger, string reason, CancellationToken ct = default)
  {
      await ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, trigger,
          "system", reason, NodeManagementAuditActions.NodeDecommissionCompleted,
          mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);

      var op = await db.Operations.AsNoTracking()
          .Where(o => o.CorrelationId == nodeId
                   && o.OperationType == "Decommission"
                   && o.Status != "Completed" && o.Status != "Cancelled" && o.Status != "Failed")
          .OrderByDescending(o => o.StartedAt)
          .FirstOrDefaultAsync(ct);

      if (op is not null)
          await operationService.CompleteAsync(op.OperationId, OperationResult.Success,
              $"Node {nodeId} fully decommissioned", ct);
  }
  ```

---

### D. Fix DecommissionOperationHandler stub

- [ ] **D1. Complete the stub in `DecommissionOperationHandler.CancelAsync`**

  Open `src/MSOSync.Metadata/Operations/Handlers/DecommissionOperationHandler.cs` (created in Task 3).

  The handler receives a `referenceId` (Guid) but decommission operations track the node by `correlationId` (the nodeId string). The `CancelAsync` on the operation service finds the open operation by `operationId`, and the handler receives the `referenceId` from the operation row. Since `referenceId` is `null` for decommission operations (we set it to null in step C4), the handler must look up the nodeId via the `correlationId` field.

  For simplicity in this task, `DecommissionOperationHandler` looks up the open operation itself to find the nodeId:

  Replace the entire file with:

  ```csharp
  using Microsoft.EntityFrameworkCore;
  using MSOSync.Metadata.NodeManagement;
  using MSOSync.Persistence;

  namespace MSOSync.Metadata.Operations.Handlers;

  /// <summary>
  /// Handles cancel for Decommission operations by transitioning the node from
  /// Decommissioning back to Disabled via INodeLifecycleService.CancelDecommissionAsync.
  ///
  /// The referenceId in the operation row is null for decommission (node IDs are strings).
  /// The actorId is used to look up the actor's username from the SyncUser table.
  /// The nodeId is carried in the operation's CorrelationId column.
  /// </summary>
  public sealed class DecommissionOperationHandler(
      INodeLifecycleService lifecycle,
      AppDbContext           db) : IOperationHandler
  {
      public OperationType OperationType => OperationType.Decommission;

      public async Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
      {
          // The referenceId is null (Guid.Empty) for decommission — nodeId lives in CorrelationId.
          // We can't directly receive the nodeId here, so we look it up from the caller's actorId
          // and the most recent open Decommission operation associated with the referenceId.
          //
          // Since referenceId == Guid.Empty for decommission, the handler receives an empty Guid.
          // The nodeId must be supplied through the correlationId of the operation row.
          // OperationService.CancelAsync calls handler.CancelAsync(op.ReferenceId.Value, ...) only
          // when op.ReferenceId.HasValue. Since we set referenceId = null, the handler is NOT called
          // by the generic path. Instead, OperationService.CancelAsync must be adapted to pass
          // the correlationId to the handler via a separate overload or lookup.
          //
          // SIMPLEST APPROACH for 12C: override CancelAsync in OperationService to pass the
          // correlationId as a string and match to the nodeId. See the note below for the
          // updated OperationService.CancelAsync pattern.
          //
          // Until that overload is available, use the actorId to find the username and cancel
          // the most recently-started open Decommission operation by correlationId:

          var actor = await db.Users.AsNoTracking()
              .Where(u => u.UserId == actorId)
              .Select(u => u.Username)
              .FirstOrDefaultAsync(ct)
              ?? actorId.ToString();

          // The correlationId of the operation is the nodeId (set in NodeLifecycleService.DecommissionAsync).
          // We must receive the nodeId from the caller. Since OperationService calls
          // handler.CancelAsync(op.ReferenceId.Value, ...) only when referenceId is non-null,
          // and decommission uses null referenceId, the OperationService.CancelAsync method
          // must be updated to also call handler.CancelAsync when CorrelationId is set and
          // ReferenceId is null. See the OperationService patch below.

          // For now, referenceId == Guid from a workaround: the string nodeId is NOT a Guid,
          // so this handler receives Guid.Empty. We need the correlationId string.
          // This is resolved by the OperationService patch in step D2.
          throw new InvalidOperationException(
              "DecommissionOperationHandler.CancelAsync requires OperationService patch (step D2).");
      }

      public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
          => throw new NotSupportedException(
              "Decommission retry is not supported. Use ForceCompleteDecommission or restart the process.");

      /// <summary>
      /// Called directly by the patched OperationService when the operation has no ReferenceId
      /// but has a CorrelationId (the nodeId string for decommission).
      /// </summary>
      public async Task CancelByCorrelationAsync(string nodeId, Guid actorId, CancellationToken ct)
      {
          var actor = await db.Users.AsNoTracking()
              .Where(u => u.UserId == actorId)
              .Select(u => u.Username)
              .FirstOrDefaultAsync(ct)
              ?? actorId.ToString();

          await lifecycle.CancelDecommissionAsync(nodeId, actor, ct);
      }
  }
  ```

- [ ] **D2. Patch OperationService.CancelAsync to handle null referenceId with correlationId**

  Open `src/MSOSync.Metadata/Operations/OperationService.cs`. In the `CancelAsync` method, replace the handler dispatch block:

  ```csharp
  // Delegate domain-side cancellation first
  if (op.ReferenceId.HasValue
      && Enum.TryParse<OperationType>(op.OperationType, out var opType))
  {
      var handler = keyedServices.GetKeyedService<IOperationHandler>(opType);
      if (handler is not null)
          await handler.CancelAsync(op.ReferenceId.Value, actorId, ct);
  }
  ```

  With:

  ```csharp
  // Delegate domain-side cancellation first
  if (Enum.TryParse<OperationType>(op.OperationType, out var opType))
  {
      var handler = keyedServices.GetKeyedService<IOperationHandler>(opType);
      if (handler is not null)
      {
          if (op.ReferenceId.HasValue)
          {
              await handler.CancelAsync(op.ReferenceId.Value, actorId, ct);
          }
          else if (!string.IsNullOrEmpty(op.CorrelationId)
                   && handler is MSOSync.Metadata.Operations.Handlers.DecommissionOperationHandler decomHandler)
          {
              // Decommission uses correlationId (nodeId string) instead of a Guid referenceId
              await decomHandler.CancelByCorrelationAsync(op.CorrelationId, actorId, ct);
          }
      }
  }
  ```

---

### E. Build all modified projects

- [ ] **E1. Build in dependency order**

  ```powershell
  dotnet build src\MSOSync.Persistence\MSOSync.Persistence.csproj
  dotnet build src\MSOSync.Metadata\MSOSync.Metadata.csproj
  dotnet build src\MSOSync.App\MSOSync.App.csproj
  dotnet build src\MSOSync.Api\MSOSync.Api.csproj
  ```

  Fix any compiler errors before proceeding. Common errors:
  - `NodeManagementAuditActions.NodeDecommissionCancelled` not found — add the constant (step C2 note).
  - `SyncUser.UserId` property name mismatch — check the actual property name in `SyncUser.cs`.

---

### F. Integration tests

- [ ] **F1. Create `tests/MSOSync.IntegrationTests/Operations/OperationsIntegrationTests.cs`**

  This file uses the same `WebApplicationFactory<Program>` pattern as `ExportJobIntegrationTests.cs`. It targets the real SQL Server LocalDB schema.

  ```csharp
  using System.Net;
  using System.Net.Http.Json;
  using System.Text.Json;
  using FluentAssertions;
  using Microsoft.AspNetCore.Mvc.Testing;
  using Microsoft.AspNetCore.TestHost;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Hosting;
  using MSOSync.App;
  using MSOSync.Common;
  using MSOSync.Metadata;
  using MSOSync.Metadata.Operations;
  using MSOSync.Persistence;
  using MSOSync.Persistence.Entities;
  using MSOSync.Security;
  using MSOSync.Topology;
  using Xunit;

  namespace MSOSync.IntegrationTests.Operations;

  // ── Fixture ───────────────────────────────────────────────────────────────────

  public sealed class OperationsFixture : WebApplicationFactory<Program>, IAsyncLifetime
  {
      private const string ConnStr =
          "Server=(localdb)\\mssqllocaldb;Database=MSOSyncOperations_Test;" +
          "Trusted_Connection=True;TrustServerCertificate=True;";

      private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

      public string AdminUsername { get; } = "ops-admin";
      public string AdminPassword { get; } = "AdminP@ss1!";
      public string ViewerUsername { get; } = "ops-viewer";
      public string ViewerPassword { get; } = "ViewP@ss1!";

      protected override IHost CreateHost(IHostBuilder builder)
      {
          builder.ConfigureServices(services =>
          {
              // Remove the real DbContext and replace with test DB
              var descriptor = services.SingleOrDefault(d =>
                  d.ServiceType == typeof(DbContextOptions<AppDbContext>));
              if (descriptor is not null) services.Remove(descriptor);

              services.AddDbContext<AppDbContext>(o =>
                  o.UseSqlServer(ConnStr));

              // Override JWT secret
              services.Configure<MSOSync.Security.JwtOptions>(o =>
              {
                  o.Secret = JwtSecret;
                  o.Issuer = "MSOSync.Test";
                  o.Audience = "MSOSync.Test";
                  o.ExpiryMinutes = 60;
              });
          });

          return base.CreateHost(builder);
      }

      public async Task InitializeAsync()
      {
          using var scope = Services.CreateScope();
          var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
          await db.Database.MigrateAsync();

          // Seed admin and viewer users
          var security = scope.ServiceProvider.GetRequiredService<IUserService>();
          if (!await db.Users.AnyAsync(u => u.Username == AdminUsername))
              await security.CreateUserAsync(AdminUsername, AdminPassword, roles: new[] { "Admin" });
          if (!await db.Users.AnyAsync(u => u.Username == ViewerUsername))
              await security.CreateUserAsync(ViewerUsername, ViewerPassword, roles: new[] { "Viewer" });
      }

      public async Task DisposeAsync()
      {
          using var scope = Services.CreateScope();
          var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
          await db.Database.EnsureDeletedAsync();
      }

      public HttpClient AdminClient() => CreateClientWithToken(AdminUsername, AdminPassword);
      public HttpClient ViewerClient() => CreateClientWithToken(ViewerUsername, ViewerPassword);

      private HttpClient CreateClientWithToken(string username, string password)
      {
          var client = CreateClient();
          var token = GetJwtToken(username, password).GetAwaiter().GetResult();
          client.DefaultRequestHeaders.Authorization =
              new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
          return client;
      }

      private async Task<string> GetJwtToken(string username, string password)
      {
          var client = CreateClient();
          var resp = await client.PostAsJsonAsync("api/v1/auth/login",
              new { username, password });
          resp.EnsureSuccessStatusCode();
          var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
          return body.GetProperty("accessToken").GetString()!;
      }
  }

  // ── Tests ─────────────────────────────────────────────────────────────────────

  [Collection("Operations")]
  public sealed class OperationsIntegrationTests(OperationsFixture fixture)
      : IClassFixture<OperationsFixture>
  {
      // ── Rollout creates an Operation row ──────────────────────────────────────

      [Fact]
      public async Task StartRollout_CreatesOperationRow_WithStatusPending()
      {
          // Arrange: create a template and a node
          using var scope = fixture.Services.CreateScope();
          var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

          var templateId = Guid.NewGuid();
          db.ConfigurationTemplates.Add(new SyncConfigurationTemplate
          {
              TemplateId    = templateId,
              TemplateName  = "ops-test-template",
              IsActive      = true,
              CreatedAt     = DateTime.UtcNow,
          });
          db.ConfigurationTemplateVersions.Add(new SyncConfigurationTemplateVersion
          {
              TemplateId    = templateId,
              Version       = 1,
              SchemaVersion = 1,
              ConfigJson    = "{}",
              CreatedAt     = DateTime.UtcNow,
          });
          db.Nodes.Add(new SyncNode
          {
              NodeId         = "ops-node-01",
              ExternalId     = "ops-node-01",
              GroupId        = "default",
              SyncUrl        = "https://ops-node-01.local:8080",
              LifecycleState = NodeLifecycleState.Active,
              NodeType       = "mssql",
              NodeName       = "ops-node-01",
          });
          await db.SaveChangesAsync();

          // Act: start a rollout via the API
          var body = new { templateId, version = 1, nodeIds = new[] { "ops-node-01" } };
          var resp = await fixture.AdminClient()
              .PostAsJsonAsync("api/v1/configuration-rollouts", body);
          resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

          // Assert: operation row was created
          await Task.Delay(200); // give the background task a moment to persist CreateAsync
          var ops = await db.Operations.AsNoTracking()
              .Where(o => o.OperationType == "Rollout")
              .ToListAsync();

          ops.Should().HaveCount(1);
          ops[0].Status.Should().BeOneOf("Pending", "Running", "Completed");
          ops[0].CanCancel.Should().BeTrue();
          ops[0].Summary.Should().Contain("Rollout template");
      }

      // ── Cancel a running rollout ──────────────────────────────────────────────

      [Fact]
      public async Task CancelRolloutOperation_UpdatesOperationStatusToCancelled()
      {
          // Seed an operation row directly (simulating a rollout that started)
          using var scope = fixture.Services.CreateScope();
          var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
          var svc = scope.ServiceProvider.GetRequiredService<IOperationService>();

          var rolloutId = Guid.NewGuid();
          db.ConfigurationRollouts.Add(new SyncConfigurationRollout
          {
              Id              = rolloutId,
              Status          = "InProgress",
              TemplateId      = Guid.NewGuid(),
              TemplateVersion = 1,
              TargetNodeCount = 1,
              InitiatedBy     = Guid.NewGuid(),
              StartedAt       = DateTime.UtcNow,
          });
          await db.SaveChangesAsync();

          var operationId = await svc.CreateAsync(
              OperationType.Rollout, rolloutId, null, OperationSource.User,
              rolloutId.ToString(), canCancel: true, canRetry: false,
              "Test rollout", null, default);

          // Act: cancel via API
          var resp = await fixture.AdminClient()
              .PostAsJsonAsync($"api/v1/operations/{operationId}/cancel", new { });

          resp.StatusCode.Should().Be(HttpStatusCode.OK);

          // Assert
          var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
          body.GetProperty("status").GetString().Should().Be("Cancelled");

          var op = await db.Operations.FindAsync(operationId);
          op!.Status.Should().Be("Cancelled");
          op.Result.Should().Be("Cancelled");
          op.CompletedAt.Should().NotBeNull();

          // Verify rollout row was also marked Cancelled
          var rollout = await db.ConfigurationRollouts.FindAsync(rolloutId);
          rollout!.Status.Should().Be("Cancelled");
      }

      // ── Viewer cannot cancel ──────────────────────────────────────────────────

      [Fact]
      public async Task CancelOperation_ViewerToken_Returns403()
      {
          using var scope = fixture.Services.CreateScope();
          var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
          var svc = scope.ServiceProvider.GetRequiredService<IOperationService>();

          var opId = await svc.CreateAsync(
              OperationType.Export, Guid.NewGuid(), null, OperationSource.User,
              "viewer-test", canCancel: true, canRetry: false, "Viewer cancel test", null, default);

          var resp = await fixture.ViewerClient()
              .PostAsJsonAsync($"api/v1/operations/{opId}/cancel", new { });

          resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
      }

      // ── List returns created operations ──────────────────────────────────────

      [Fact]
      public async Task GetOperations_AdminToken_Returns200WithItems()
      {
          using var scope = fixture.Services.CreateScope();
          var svc = scope.ServiceProvider.GetRequiredService<IOperationService>();
          await svc.CreateAsync(OperationType.Export, null, null, OperationSource.Api,
              "list-test", false, false, "List test op", null, default);

          var resp = await fixture.AdminClient().GetAsync("api/v1/operations");
          resp.StatusCode.Should().Be(HttpStatusCode.OK);

          var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
          body.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
      }
  }
  ```

- [ ] **F2. Run the integration tests**

  ```powershell
  dotnet test tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj `
      --filter "FullyQualifiedName~Operations" `
      --logger "console;verbosity=detailed"
  ```

  All four integration tests must pass. If `IUserService` does not exist, replace the seeding with direct `db.Users.Add` + `db.SaveChangesAsync` calls using the pattern from existing integration test fixtures.

- [ ] **F3. Run the full unit test suite**

  ```powershell
  dotnet test tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj
  ```

  All previously passing tests must still pass. The `OperationService` mock in `OperationServiceTests` (Task 3) must still pass — adding `AppDbContext.Operations` does not break SQLite-based tests.

---

### G. Commit

- [ ] **G1. Stage and commit all changed files**

  ```powershell
  git add src\MSOSync.App\Export\ExportJobService.cs `
          src\MSOSync.Metadata\Configuration\RolloutService.cs `
          src\MSOSync.Metadata\NodeManagement\INodeLifecycleService.cs `
          src\MSOSync.Metadata\NodeManagement\NodeLifecycleService.cs `
          src\MSOSync.Metadata\Operations\Handlers\DecommissionOperationHandler.cs `
          src\MSOSync.Metadata\Operations\OperationService.cs `
          tests\MSOSync.IntegrationTests\Operations\OperationsIntegrationTests.cs
  git commit -m "feat(12C-5): wire IOperationService into Export/Rollout/Decommission + integration tests"
  ```

---

## Acceptance criteria

- `dotnet build` passes for all four projects (`Persistence`, `Metadata`, `App`, `Api`) with 0 errors.
- `ExportJobService.CreateJobAsync` creates a `sync_operation` row with `OperationType = 'Export'`.
- `ExportJobService.CompleteJobAsync` closes the operation row with `Status = 'Completed'`, `Result = 'Success'`.
- `ExportJobService.FailJobAsync` closes the operation row with `Status = 'Failed'`, `Result = 'Failure'`.
- `RolloutService.StartRolloutAsync` creates a `sync_operation` row with `OperationType = 'Rollout'`, `CanCancel = true`.
- The background rollout loop calls `IOperationService.UpdateProgressAsync` after each node.
- The background rollout loop calls `IOperationService.CompleteAsync` with `PartialSuccess` when some nodes failed.
- `NodeLifecycleService.DecommissionAsync` creates a `sync_operation` row with `OperationType = 'Decommission'`.
- `NodeLifecycleService.FinalizeDecommissionAsync` closes the operation row with `Status = 'Completed'`.
- `INodeLifecycleService.CancelDecommissionAsync` is declared in the interface and implemented.
- `DecommissionOperationHandler.CancelByCorrelationAsync` calls `lifecycle.CancelDecommissionAsync(nodeId, ...)`.
- `POST /api/v1/operations/{id}/cancel` on a Rollout operation marks both the `sync_operation` row and the `sync_configuration_rollout` row as Cancelled.
- All four integration tests pass against a real LocalDB instance.
- All pre-existing unit and integration tests still pass.
