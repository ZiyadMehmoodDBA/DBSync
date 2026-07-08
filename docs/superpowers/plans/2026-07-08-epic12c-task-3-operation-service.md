# Epic 12C — Task 3: IOperationService + Handlers + Query Service

**Branch:** `feat/epic12c-system-admin`  
**Files touched:** 13 new, 2 modified  
**Depends on:** Task 1 complete (`SyncOperation` entity + `AppDbContext.Operations` available).

---

## Context

This task builds the full service layer for the Operations subsystem:

1. **IOperationService / OperationService** — CRUD lifecycle for a `sync_operation` row (Create, UpdateProgress, Complete, Cancel, Retry).
2. **IOperationHandler + three handlers** — keyed-DI strategy pattern; each domain service (Export, Rollout, Decommission) owns its own cancel/retry behaviour.
3. **IOperationQueryService / OperationQueryService** — read-side with cursor pagination and detail lookup.
4. **MediatR OperationChangedEvent + Publisher** — SignalR push to a dedicated `"operators"` group.
5. **Unit tests** in `tests/MSOSync.MetadataTests/Operations/`.

The handler registry uses .NET 8+ keyed services (`AddKeyedScoped`), resolved at runtime via `IKeyedServiceProvider`.

---

## Steps

### A. Shared enum types

- [ ] **A1. Create `src/MSOSync.Metadata/Operations/OperationEnums.cs`**

  ```csharp
  namespace MSOSync.Metadata.Operations;

  public enum OperationType
  {
      Export,
      Rollout,
      Decommission,
      Recovery,
  }

  public enum OperationSource
  {
      User,
      System,
      Scheduler,
      Worker,
      Api,
  }

  public enum OperationStatus
  {
      Pending,
      Running,
      Completed,
      Failed,
      Cancelled,
  }

  public enum OperationResult
  {
      Success,
      PartialSuccess,
      Failure,
      Cancelled,
  }
  ```

### B. DTOs

- [ ] **B1. Create `src/MSOSync.Metadata/Operations/OperationDto.cs`**

  ```csharp
  namespace MSOSync.Metadata.Operations;

  public sealed record OperationDto(
      Guid            OperationId,
      string          OperationType,
      Guid?           ReferenceId,
      string          Status,
      string?         Result,
      string          Source,
      int?            ProgressPercent,
      string?         ProgressMessage,
      string?         CorrelationId,
      Guid?           InitiatedBy,
      string?         MetadataJson,
      string?         Summary,
      bool            CanCancel,
      bool            CanRetry,
      DateTime        StartedAt,
      DateTime?       CompletedAt,
      int?            QueuePosition   // non-null only for Pending operations
  );

  public sealed record OperationPageDto(
      IReadOnlyList<OperationDto> Items,
      string?                     NextCursor,
      int?                        TotalCount);

  public sealed record OperationDetailDto(
      Guid            OperationId,
      string          OperationType,
      Guid?           ReferenceId,
      string          Status,
      string?         Result,
      string          Source,
      int?            ProgressPercent,
      string?         ProgressMessage,
      string?         CorrelationId,
      Guid?           InitiatedBy,
      string?         MetadataJson,
      string?         Summary,
      bool            CanCancel,
      bool            CanRetry,
      DateTime        StartedAt,
      DateTime?       CompletedAt);

  public sealed record OperationFilter(
      string[]?  Types      = null,
      string[]?  Statuses   = null,
      string[]?  Sources    = null,
      DateTime?  From       = null,
      DateTime?  To         = null,
      string?    InitiatedBy = null,
      string?    Cursor     = null,
      int        PageSize   = 25);
  ```

### C. IOperationService interface

- [ ] **C1. Create `src/MSOSync.Metadata/Operations/IOperationService.cs`**

  ```csharp
  namespace MSOSync.Metadata.Operations;

  public interface IOperationService
  {
      /// <summary>
      /// Persists a new sync_operation row in Pending status and returns its ID.
      /// Call this at the START of a long-running job before returning to the caller.
      /// </summary>
      Task<Guid> CreateAsync(
          OperationType   type,
          Guid?           referenceId,
          Guid?           initiatedBy,
          OperationSource source,
          string          correlationId,
          bool            canCancel,
          bool            canRetry,
          string          summary,
          string?         metadataJson,
          CancellationToken ct);

      /// <summary>Updates progress_percent and progress_message. Status stays Running.</summary>
      Task UpdateProgressAsync(Guid operationId, int percent, string? message, CancellationToken ct);

      /// <summary>
      /// Marks the operation Completed and sets result + completed_at.
      /// Pass a new summary if the final summary differs from the initial one.
      /// </summary>
      Task CompleteAsync(Guid operationId, OperationResult result, string? summary, CancellationToken ct);

      /// <summary>
      /// Cancels a Pending or Running operation. Delegates to the domain handler
      /// for domain-side cancellation logic, then marks the row Cancelled.
      /// Throws InvalidOperationException if the operation's current status does not
      /// allow cancellation (i.e. it is already terminal).
      /// </summary>
      Task CancelAsync(Guid operationId, Guid actorId, CancellationToken ct);

      /// <summary>
      /// Retries a Failed or Cancelled operation by resetting it to Pending and
      /// delegating to the domain handler to re-enqueue the work.
      /// Throws InvalidOperationException if can_retry = false or status is not retryable.
      /// </summary>
      Task RetryAsync(Guid operationId, Guid actorId, CancellationToken ct);
  }
  ```

### D. IOperationHandler interface and three handlers

- [ ] **D1. Create `src/MSOSync.Metadata/Operations/IOperationHandler.cs`**

  ```csharp
  namespace MSOSync.Metadata.Operations;

  /// <summary>
  /// Strategy interface implemented by each domain service that can own an operation.
  /// Registered as keyed-scoped by OperationType.
  /// </summary>
  public interface IOperationHandler
  {
      OperationType OperationType { get; }

      /// <summary>
      /// Performs domain-level cancellation (e.g. marks rollout as cancelled in DB).
      /// Called by OperationService.CancelAsync BEFORE the operation row is updated.
      /// </summary>
      Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct);

      /// <summary>
      /// Re-enqueues or re-starts the domain work for a retry.
      /// Called by OperationService.RetryAsync BEFORE the operation row is reset to Pending.
      /// </summary>
      Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct);
  }
  ```

- [ ] **D2. Create `src/MSOSync.Metadata/Operations/Handlers/ExportOperationHandler.cs`**

  ```csharp
  using MSOSync.Metadata.Export;

  namespace MSOSync.Metadata.Operations.Handlers;

  /// <summary>
  /// Delegates Export operation cancel/retry to IExportJobService.
  /// The referenceId is the SyncExportJob.JobId.
  /// </summary>
  public sealed class ExportOperationHandler(IExportJobService exportJobService) : IOperationHandler
  {
      public OperationType OperationType => OperationType.Export;

      public async Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
      {
          // SoftDeleteJobAsync sets status = Deleted, which halts the worker loop.
          // If the job is already terminal, this is a no-op inside SoftDeleteJobAsync.
          await exportJobService.SoftDeleteJobAsync(referenceId, ct);
      }

      public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
      {
          // Export retry: reset the export job row back to Pending so the worker picks it up.
          // IExportJobService does not expose a ResetToPendingAsync today; this is a stub
          // that throws until the export service implements it (tracked as 12C tech-debt item).
          throw new NotSupportedException(
              "Export job retry is not yet implemented. " +
              "Create a new export job instead of retrying.");
      }
  }
  ```

- [ ] **D3. Create `src/MSOSync.Metadata/Operations/Handlers/RolloutOperationHandler.cs`**

  ```csharp
  using Microsoft.EntityFrameworkCore;
  using MSOSync.Persistence;

  namespace MSOSync.Metadata.Operations.Handlers;

  /// <summary>
  /// Delegates Rollout operation cancel to the sync_configuration_rollout table.
  /// The referenceId is the SyncConfigurationRollout.Id (rollout_id).
  /// </summary>
  public sealed class RolloutOperationHandler(AppDbContext db) : IOperationHandler
  {
      public OperationType OperationType => OperationType.Rollout;

      public async Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
      {
          // Mark the rollout row as Cancelled. The background fire-and-forget loop
          // in RolloutService checks Status and aborts when it sees a non-InProgress value.
          var updated = await db.ConfigurationRollouts
              .Where(r => r.Id == referenceId && r.Status == "InProgress")
              .ExecuteUpdateAsync(s =>
                  s.SetProperty(r => r.Status,      "Cancelled")
                   .SetProperty(r => r.CompletedAt, DateTime.UtcNow),
                  ct);

          if (updated == 0)
          {
              // Either not found or already in a terminal state — treat as idempotent.
              // Do not throw: the operation row will still be marked Cancelled by OperationService.
          }
      }

      public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
      {
          // Rollout retry is not safe to implement generically without knowing which
          // nodes still need to be addressed. The operator should create a new rollout.
          throw new NotSupportedException(
              "Rollout retry is not supported. Create a new rollout targeting the failed nodes.");
      }
  }
  ```

- [ ] **D4. Create `src/MSOSync.Metadata/Operations/Handlers/DecommissionOperationHandler.cs`**

  ```csharp
  using MSOSync.Metadata.NodeManagement;

  namespace MSOSync.Metadata.Operations.Handlers;

  /// <summary>
  /// Delegates Decommission operation cancel to INodeLifecycleService.
  /// The referenceId is the SyncNode.NodeId encoded as a GUID (see note below).
  /// </summary>
  public sealed class DecommissionOperationHandler(INodeLifecycleService lifecycle) : IOperationHandler
  {
      public OperationType OperationType => OperationType.Decommission;

      public async Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
      {
          // NodeId is varchar(50), not a GUID. The referenceId here is the operation's
          // reference_id column, which for decommission was set to the node's internal
          // metadata_json when CreateAsync was called by NodeLifecycleService.
          //
          // For now, CancelDecommissionAsync is a stub — it must be added to INodeLifecycleService
          // in Task 5. This placeholder throws until that method is wired.
          //
          // When Task 5 is complete, replace this with:
          //   await lifecycle.CancelDecommissionAsync(referenceId.ToString(), actorId.ToString(), ct);
          throw new NotSupportedException(
              "Decommission cancellation via INodeLifecycleService.CancelDecommissionAsync " +
              "is wired in Task 5 (epic12c-task-5-domain-integration).");
      }

      public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
      {
          throw new NotSupportedException(
              "Decommission retry is not supported. Use ForceCompleteDecommission or restart the process.");
      }
  }
  ```

### E. OperationService implementation

- [ ] **E1. Create `src/MSOSync.Metadata/Operations/OperationService.cs`**

  ```csharp
  using MediatR;
  using Microsoft.EntityFrameworkCore;
  using MSOSync.Persistence;
  using MSOSync.Persistence.Entities;

  namespace MSOSync.Metadata.Operations;

  public sealed class OperationService(
      AppDbContext             db,
      IPublisher               publisher,
      IKeyedServiceProvider    keyedServices) : IOperationService
  {
      public async Task<Guid> CreateAsync(
          OperationType   type,
          Guid?           referenceId,
          Guid?           initiatedBy,
          OperationSource source,
          string          correlationId,
          bool            canCancel,
          bool            canRetry,
          string          summary,
          string?         metadataJson,
          CancellationToken ct)
      {
          var op = new SyncOperation
          {
              OperationId     = Guid.NewGuid(),
              OperationType   = type.ToString(),
              ReferenceId     = referenceId,
              Status          = OperationStatus.Pending.ToString(),
              Result          = null,
              Source          = source.ToString(),
              ProgressPercent = null,
              ProgressMessage = null,
              CorrelationId   = correlationId,
              InitiatedBy     = initiatedBy,
              MetadataJson    = metadataJson,
              Summary         = summary,
              CanCancel       = canCancel,
              CanRetry        = canRetry,
              StartedAt       = DateTime.UtcNow,
              CompletedAt     = null,
          };

          db.Operations.Add(op);
          await db.SaveChangesAsync(ct);

          await publisher.Publish(
              new OperationChangedEvent(op.OperationId, op.OperationType, op.Status), ct);

          return op.OperationId;
      }

      public async Task UpdateProgressAsync(
          Guid operationId, int percent, string? message, CancellationToken ct)
      {
          await db.Operations
              .Where(o => o.OperationId == operationId)
              .ExecuteUpdateAsync(s => s
                  .SetProperty(o => o.ProgressPercent,  percent)
                  .SetProperty(o => o.ProgressMessage,  message)
                  .SetProperty(o => o.Status,           OperationStatus.Running.ToString()),
              ct);

          await PublishChangedAsync(operationId, ct);
      }

      public async Task CompleteAsync(
          Guid operationId, OperationResult result, string? summary, CancellationToken ct)
      {
          var resultStr = result.ToString();
          var status    = result == OperationResult.Success || result == OperationResult.PartialSuccess
              ? OperationStatus.Completed.ToString()
              : OperationStatus.Failed.ToString();

          await db.Operations
              .Where(o => o.OperationId == operationId)
              .ExecuteUpdateAsync(s => s
                  .SetProperty(o => o.Status,          status)
                  .SetProperty(o => o.Result,          resultStr)
                  .SetProperty(o => o.CompletedAt,     DateTime.UtcNow)
                  .SetProperty(o => o.ProgressPercent, 100)
                  .SetProperty(o => o.Summary,         summary ?? o.Summary),
              ct);

          await PublishChangedAsync(operationId, ct);
      }

      public async Task CancelAsync(Guid operationId, Guid actorId, CancellationToken ct)
      {
          var op = await db.Operations.FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
              ?? throw new KeyNotFoundException($"Operation {operationId} not found.");

          if (op.Status is not ("Pending" or "Running"))
              throw new InvalidOperationException(
                  $"Operation {operationId} is in status '{op.Status}' and cannot be cancelled.");

          if (!op.CanCancel)
              throw new InvalidOperationException(
                  $"Operation {operationId} does not support cancellation.");

          // Delegate domain-side cancellation first
          if (op.ReferenceId.HasValue
              && Enum.TryParse<OperationType>(op.OperationType, out var opType))
          {
              var handler = keyedServices.GetKeyedService<IOperationHandler>(opType);
              if (handler is not null)
                  await handler.CancelAsync(op.ReferenceId.Value, actorId, ct);
          }

          await db.Operations
              .Where(o => o.OperationId == operationId)
              .ExecuteUpdateAsync(s => s
                  .SetProperty(o => o.Status,      OperationStatus.Cancelled.ToString())
                  .SetProperty(o => o.Result,      OperationResult.Cancelled.ToString())
                  .SetProperty(o => o.CompletedAt, DateTime.UtcNow),
              ct);

          await PublishChangedAsync(operationId, ct);
      }

      public async Task RetryAsync(Guid operationId, Guid actorId, CancellationToken ct)
      {
          var op = await db.Operations.FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
              ?? throw new KeyNotFoundException($"Operation {operationId} not found.");

          if (op.Status is not ("Failed" or "Cancelled"))
              throw new InvalidOperationException(
                  $"Operation {operationId} is in status '{op.Status}' and cannot be retried.");

          if (!op.CanRetry)
              throw new InvalidOperationException(
                  $"Operation {operationId} does not support retry.");

          // Delegate domain-side retry first
          if (op.ReferenceId.HasValue
              && Enum.TryParse<OperationType>(op.OperationType, out var opType))
          {
              var handler = keyedServices.GetKeyedService<IOperationHandler>(opType);
              if (handler is not null)
                  await handler.RetryAsync(op.ReferenceId.Value, actorId, ct);
          }

          // Reset to Pending so the domain worker can pick it up
          await db.Operations
              .Where(o => o.OperationId == operationId)
              .ExecuteUpdateAsync(s => s
                  .SetProperty(o => o.Status,          OperationStatus.Pending.ToString())
                  .SetProperty(o => o.Result,          (string?)null)
                  .SetProperty(o => o.CompletedAt,     (DateTime?)null)
                  .SetProperty(o => o.ProgressPercent, (int?)null)
                  .SetProperty(o => o.ProgressMessage, (string?)null),
              ct);

          await PublishChangedAsync(operationId, ct);
      }

      // ── Private helpers ────────────────────────────────────────────────────────

      private async Task PublishChangedAsync(Guid operationId, CancellationToken ct)
      {
          var op = await db.Operations.AsNoTracking()
              .FirstOrDefaultAsync(o => o.OperationId == operationId, ct);
          if (op is null) return;

          await publisher.Publish(
              new OperationChangedEvent(op.OperationId, op.OperationType, op.Status), ct);
      }
  }
  ```

### F. IOperationQueryService + OperationQueryService

- [ ] **F1. Create `src/MSOSync.Metadata/Operations/IOperationQueryService.cs`**

  ```csharp
  namespace MSOSync.Metadata.Operations;

  public interface IOperationQueryService
  {
      Task<OperationPageDto>    GetPageAsync(OperationFilter filter, CancellationToken ct);
      Task<OperationDetailDto?> GetDetailAsync(Guid operationId, CancellationToken ct);
  }
  ```

- [ ] **F2. Create `src/MSOSync.Metadata/Operations/OperationQueryService.cs`**

  ```csharp
  using System.Text;
  using Microsoft.EntityFrameworkCore;
  using MSOSync.Persistence;

  namespace MSOSync.Metadata.Operations;

  public sealed class OperationQueryService(AppDbContext db) : IOperationQueryService
  {
      public async Task<OperationPageDto> GetPageAsync(OperationFilter filter, CancellationToken ct)
      {
          var pageSize = Math.Clamp(filter.PageSize, 1, 100);

          var query = db.Operations.AsNoTracking().AsQueryable();

          if (filter.Types is { Length: > 0 })
              query = query.Where(o => filter.Types.Contains(o.OperationType));

          if (filter.Statuses is { Length: > 0 })
              query = query.Where(o => filter.Statuses.Contains(o.Status));

          if (filter.Sources is { Length: > 0 })
              query = query.Where(o => filter.Sources.Contains(o.Source));

          if (filter.From.HasValue)
              query = query.Where(o => o.StartedAt >= filter.From.Value);

          if (filter.To.HasValue)
              query = query.Where(o => o.StartedAt <= filter.To.Value);

          if (!string.IsNullOrEmpty(filter.InitiatedBy)
              && Guid.TryParse(filter.InitiatedBy, out var initiatedByGuid))
              query = query.Where(o => o.InitiatedBy == initiatedByGuid);

          // Cursor is the StartedAt tick value of the last item, encoded as base64
          if (!string.IsNullOrEmpty(filter.Cursor) && TryDecodeCursor(filter.Cursor, out var cursorTick))
          {
              var cursorDate = new DateTime(cursorTick, DateTimeKind.Utc);
              query = query.Where(o => o.StartedAt < cursorDate);
          }

          query = query.OrderByDescending(o => o.StartedAt);

          // Fetch one extra to detect next page
          var rows = await query.Take(pageSize + 1).ToListAsync(ct);

          string? nextCursor = null;
          if (rows.Count > pageSize)
          {
              rows.RemoveAt(pageSize);
              nextCursor = EncodeCursor(rows[^1].StartedAt.Ticks);
          }

          // Compute queue position for Pending operations
          var pendingCount = await db.Operations.AsNoTracking()
              .CountAsync(o => o.Status == "Pending", ct);

          var pendingRank = 0;
          var items = rows.Select(o =>
          {
              int? queuePos = null;
              if (o.Status == "Pending") queuePos = ++pendingRank;
              return new OperationDto(
                  o.OperationId, o.OperationType, o.ReferenceId,
                  o.Status, o.Result, o.Source,
                  o.ProgressPercent, o.ProgressMessage,
                  o.CorrelationId, o.InitiatedBy,
                  o.MetadataJson, o.Summary,
                  o.CanCancel, o.CanRetry,
                  o.StartedAt, o.CompletedAt,
                  QueuePosition: queuePos);
          }).ToList();

          return new OperationPageDto(items, nextCursor, TotalCount: null);
      }

      public async Task<OperationDetailDto?> GetDetailAsync(Guid operationId, CancellationToken ct)
      {
          var o = await db.Operations.AsNoTracking()
              .FirstOrDefaultAsync(x => x.OperationId == operationId, ct);
          if (o is null) return null;

          return new OperationDetailDto(
              o.OperationId, o.OperationType, o.ReferenceId,
              o.Status, o.Result, o.Source,
              o.ProgressPercent, o.ProgressMessage,
              o.CorrelationId, o.InitiatedBy,
              o.MetadataJson, o.Summary,
              o.CanCancel, o.CanRetry,
              o.StartedAt, o.CompletedAt);
      }

      // ── Cursor encoding ────────────────────────────────────────────────────────

      private static string EncodeCursor(long ticks)
          => Convert.ToBase64String(BitConverter.GetBytes(ticks));

      private static bool TryDecodeCursor(string cursor, out long ticks)
      {
          ticks = 0;
          try
          {
              var bytes = Convert.FromBase64String(cursor);
              if (bytes.Length != 8) return false;
              ticks = BitConverter.ToInt64(bytes, 0);
              return true;
          }
          catch { return false; }
      }
  }
  ```

### G. SignalR event + publisher

- [ ] **G1. Create `src/MSOSync.App/SignalR/OperationChangedEvent.cs`**

  ```csharp
  using MediatR;

  namespace MSOSync.App.SignalR;

  /// <summary>
  /// Published by OperationService after every state change to a sync_operation row.
  /// Handled by OperationChangedPublisher which fans the event out over SignalR.
  /// </summary>
  public sealed record OperationChangedEvent(
      Guid   OperationId,
      string OperationType,
      string Status) : INotification;
  ```

- [ ] **G2. Create `src/MSOSync.App/SignalR/OperationChangedPublisher.cs`**

  ```csharp
  using MediatR;
  using Microsoft.AspNetCore.SignalR;
  using MSOSync.App.Hubs;

  namespace MSOSync.App.SignalR;

  public sealed class OperationChangedPublisher(IHubContext<OperationsHub> hub)
      : INotificationHandler<OperationChangedEvent>
  {
      public async Task Handle(OperationChangedEvent n, CancellationToken ct)
          => await hub.Clients.Group("operators")
              .SendAsync("OperationChanged", new
              {
                  operationId   = n.OperationId,
                  operationType = n.OperationType,
                  status        = n.Status,
              }, ct);
  }
  ```

- [ ] **G3. Add `OperationChanged` to `OperationsEventType`**

  Open `src/MSOSync.App/SignalR/OperationsEventType.cs` and add `OperationChanged` to the enum:

  ```csharp
  namespace MSOSync.App.SignalR;

  public enum OperationsEventType
  {
      NodeHealthChanged,
      NodeApproved,
      NodeRejected,
      NodeDisabled,
      NodeEnabled,
      SyncCycleCompleted,
      NodeLifecycleChanged,
      NodeMaintenanceChanged,
      ConfigurationChanged,
      OperationChanged,           // ← add this
  }
  ```

### H. DI registration

- [ ] **H1. Add registrations to MetadataServiceExtensions.cs**

  Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`. Add the following `using` directives at the top (after the existing ones):

  ```csharp
  using MSOSync.Metadata.Operations;
  using MSOSync.Metadata.Operations.Handlers;
  ```

  Then add the following block at the end of `AddMetadata`, just before `return services;`:

  ```csharp
  // Epic 12C — Operations registry
  services.AddScoped<IOperationService, OperationService>();
  services.AddKeyedScoped<IOperationHandler, ExportOperationHandler>(OperationType.Export);
  services.AddKeyedScoped<IOperationHandler, RolloutOperationHandler>(OperationType.Rollout);
  services.AddKeyedScoped<IOperationHandler, DecommissionOperationHandler>(OperationType.Decommission);
  services.AddScoped<IOperationQueryService, OperationQueryService>();
  ```

  The `IKeyedServiceProvider` used in `OperationService` is automatically available as a singleton in .NET 8+ DI; no additional registration needed.

### I. Unit tests

- [ ] **I1. Create `tests/MSOSync.MetadataTests/Operations/OperationServiceTests.cs`**

  ```csharp
  using FluentAssertions;
  using MediatR;
  using Moq;
  using MSOSync.Metadata.Operations;
  using MSOSync.Persistence;
  using MSOSync.Persistence.Entities;
  using Xunit;

  namespace MSOSync.MetadataTests.Operations;

  public sealed class OperationServiceTests : IDisposable
  {
      private readonly AppDbContext          _db;
      private readonly Mock<IPublisher>      _publisherMock;
      private readonly Mock<IKeyedServiceProvider> _keyedMock;
      private readonly OperationService      _sut;

      public OperationServiceTests()
      {
          _db            = TestDbContext.Create();
          _publisherMock = new Mock<IPublisher>();
          _keyedMock     = new Mock<IKeyedServiceProvider>();
          _sut           = new OperationService(_db, _publisherMock.Object, _keyedMock.Object);
      }

      public void Dispose() => _db.Dispose();

      [Fact]
      public async Task CreateAsync_PersistsRow_ReturnsNewGuid()
      {
          var id = await _sut.CreateAsync(
              OperationType.Export,
              referenceId:   Guid.NewGuid(),
              initiatedBy:   Guid.NewGuid(),
              source:        OperationSource.User,
              correlationId: "corr-001",
              canCancel:     true,
              canRetry:      false,
              summary:       "Export events to CSV",
              metadataJson:  null,
              ct:            default);

          id.Should().NotBeEmpty();

          var row = await _db.Operations.FindAsync(id);
          row.Should().NotBeNull();
          row!.Status.Should().Be("Pending");
          row.OperationType.Should().Be("Export");
          row.CanCancel.Should().BeTrue();
          row.CompletedAt.Should().BeNull();
      }

      [Fact]
      public async Task CreateAsync_PublishesOperationChangedEvent()
      {
          await _sut.CreateAsync(
              OperationType.Rollout, null, null,
              OperationSource.System, "corr-002",
              canCancel: false, canRetry: false,
              "Rollout started", null, default);

          _publisherMock.Verify(p =>
              p.Publish(It.IsAny<MSOSync.App.SignalR.OperationChangedEvent>(), default),
              Times.Once);
      }

      [Fact]
      public async Task CompleteAsync_SetsStatusAndCompletedAt()
      {
          var id = await _sut.CreateAsync(
              OperationType.Export, null, null,
              OperationSource.Worker, "corr-003",
              canCancel: false, canRetry: false,
              "Export running", null, default);

          await _sut.CompleteAsync(id, OperationResult.Success, "Export done", default);

          var row = await _db.Operations.FindAsync(id);
          row!.Status.Should().Be("Completed");
          row.Result.Should().Be("Success");
          row.CompletedAt.Should().NotBeNull();
          row.ProgressPercent.Should().Be(100);
      }

      [Fact]
      public async Task CompleteAsync_WithFailure_SetsStatusFailed()
      {
          var id = await _sut.CreateAsync(
              OperationType.Decommission, null, null,
              OperationSource.System, "corr-004",
              canCancel: false, canRetry: true,
              "Decommission running", null, default);

          await _sut.CompleteAsync(id, OperationResult.Failure, "timeout", default);

          var row = await _db.Operations.FindAsync(id);
          row!.Status.Should().Be("Failed");
          row.Result.Should().Be("Failure");
      }

      [Fact]
      public async Task CancelAsync_PendingOperation_SetsStatusCancelled()
      {
          var refId = Guid.NewGuid();
          var id = await _sut.CreateAsync(
              OperationType.Rollout, refId, null,
              OperationSource.User, "corr-005",
              canCancel: true, canRetry: false,
              "Rollout", null, default);

          // No handler registered — GetKeyedService returns null — that is fine
          _keyedMock.Setup(k => k.GetKeyedService(typeof(IOperationHandler), OperationType.Rollout))
              .Returns((object?)null);

          await _sut.CancelAsync(id, Guid.NewGuid(), default);

          var row = await _db.Operations.FindAsync(id);
          row!.Status.Should().Be("Cancelled");
          row.Result.Should().Be("Cancelled");
          row.CompletedAt.Should().NotBeNull();
      }

      [Fact]
      public async Task CancelAsync_CompletedOperation_ThrowsInvalidOperation()
      {
          var id = await _sut.CreateAsync(
              OperationType.Export, null, null,
              OperationSource.User, "corr-006",
              canCancel: true, canRetry: false,
              "Export", null, default);

          await _sut.CompleteAsync(id, OperationResult.Success, null, default);

          var act = () => _sut.CancelAsync(id, Guid.NewGuid(), default);
          await act.Should().ThrowAsync<InvalidOperationException>()
              .WithMessage("*cannot be cancelled*");
      }

      [Fact]
      public async Task CancelAsync_CanCancelFalse_ThrowsInvalidOperation()
      {
          var id = await _sut.CreateAsync(
              OperationType.Export, null, null,
              OperationSource.User, "corr-007",
              canCancel: false, canRetry: false,
              "Export", null, default);

          var act = () => _sut.CancelAsync(id, Guid.NewGuid(), default);
          await act.Should().ThrowAsync<InvalidOperationException>()
              .WithMessage("*does not support cancellation*");
      }

      [Fact]
      public async Task CancelAsync_DelegatesHandlerCancelBeforeRowUpdate()
      {
          var refId   = Guid.NewGuid();
          var actorId = Guid.NewGuid();
          var handlerMock = new Mock<IOperationHandler>();
          handlerMock.Setup(h => h.CancelAsync(refId, actorId, default)).Returns(Task.CompletedTask);

          _keyedMock
              .Setup(k => k.GetKeyedService(typeof(IOperationHandler), OperationType.Rollout))
              .Returns(handlerMock.Object);

          var id = await _sut.CreateAsync(
              OperationType.Rollout, refId, null,
              OperationSource.User, "corr-008",
              canCancel: true, canRetry: false,
              "Rollout", null, default);

          await _sut.CancelAsync(id, actorId, default);

          handlerMock.Verify(h => h.CancelAsync(refId, actorId, default), Times.Once);
      }
  }
  ```

- [ ] **I2. Create `tests/MSOSync.MetadataTests/Operations/OperationQueryServiceTests.cs`**

  ```csharp
  using FluentAssertions;
  using MSOSync.Metadata.Operations;
  using MSOSync.Persistence;
  using MSOSync.Persistence.Entities;
  using Xunit;

  namespace MSOSync.MetadataTests.Operations;

  public sealed class OperationQueryServiceTests : IDisposable
  {
      private readonly AppDbContext          _db;
      private readonly OperationQueryService _sut;

      public OperationQueryServiceTests()
      {
          _db  = TestDbContext.Create();
          _sut = new OperationQueryService(_db);
      }

      public void Dispose() => _db.Dispose();

      private SyncOperation MakeOp(string type = "Export", string status = "Completed") => new()
      {
          OperationId   = Guid.NewGuid(),
          OperationType = type,
          Status        = status,
          Source        = "User",
          CanCancel     = false,
          CanRetry      = false,
          StartedAt     = DateTime.UtcNow,
      };

      [Fact]
      public async Task GetPageAsync_NoFilter_ReturnsAllOrderedByStartedAtDesc()
      {
          var early = MakeOp(); early.StartedAt = DateTime.UtcNow.AddMinutes(-5);
          var late  = MakeOp(); late.StartedAt  = DateTime.UtcNow;
          _db.Operations.AddRange(early, late);
          await _db.SaveChangesAsync();

          var result = await _sut.GetPageAsync(new OperationFilter(), default);

          result.Items.Should().HaveCount(2);
          result.Items[0].OperationId.Should().Be(late.OperationId);
          result.Items[1].OperationId.Should().Be(early.OperationId);
      }

      [Fact]
      public async Task GetPageAsync_TypeFilter_ReturnsOnlyMatchingType()
      {
          _db.Operations.AddRange(MakeOp("Export"), MakeOp("Rollout"), MakeOp("Export"));
          await _db.SaveChangesAsync();

          var result = await _sut.GetPageAsync(new OperationFilter(Types: new[] { "Export" }), default);

          result.Items.Should().HaveCount(2);
          result.Items.Should().OnlyContain(o => o.OperationType == "Export");
      }

      [Fact]
      public async Task GetPageAsync_CursorPagination_ReturnsNextPage()
      {
          for (int i = 0; i < 5; i++)
          {
              var op = MakeOp();
              op.StartedAt = DateTime.UtcNow.AddMinutes(-i);
              _db.Operations.Add(op);
          }
          await _db.SaveChangesAsync();

          var page1 = await _sut.GetPageAsync(new OperationFilter(PageSize: 3), default);
          page1.Items.Should().HaveCount(3);
          page1.NextCursor.Should().NotBeNull();

          var page2 = await _sut.GetPageAsync(
              new OperationFilter(PageSize: 3, Cursor: page1.NextCursor), default);
          page2.Items.Should().HaveCount(2);
          page2.NextCursor.Should().BeNull();
      }

      [Fact]
      public async Task GetDetailAsync_ExistingId_ReturnsDetail()
      {
          var op = MakeOp();
          _db.Operations.Add(op);
          await _db.SaveChangesAsync();

          var detail = await _sut.GetDetailAsync(op.OperationId, default);

          detail.Should().NotBeNull();
          detail!.OperationId.Should().Be(op.OperationId);
      }

      [Fact]
      public async Task GetDetailAsync_MissingId_ReturnsNull()
      {
          var result = await _sut.GetDetailAsync(Guid.NewGuid(), default);
          result.Should().BeNull();
      }

      [Fact]
      public async Task GetPageAsync_PendingItems_HaveQueuePosition()
      {
          _db.Operations.AddRange(
              new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export",
                  Status = "Pending", Source = "User", CanCancel = false, CanRetry = false,
                  StartedAt = DateTime.UtcNow.AddMinutes(-2) },
              new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Rollout",
                  Status = "Pending", Source = "User", CanCancel = false, CanRetry = false,
                  StartedAt = DateTime.UtcNow.AddMinutes(-1) });
          await _db.SaveChangesAsync();

          var result = await _sut.GetPageAsync(
              new OperationFilter(Statuses: new[] { "Pending" }), default);

          result.Items.Should().HaveCount(2);
          result.Items.Should().OnlyContain(o => o.QueuePosition.HasValue);
      }
  }
  ```

- [ ] **I3. Build and run the new tests**

  ```powershell
  dotnet build src\MSOSync.Metadata\MSOSync.Metadata.csproj
  dotnet build src\MSOSync.App\MSOSync.App.csproj
  dotnet test tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj
  ```

  All 12+ new tests must pass. Pre-existing tests must still pass.

- [ ] **I4. Commit**

  ```powershell
  git add src\MSOSync.Metadata\Operations\ `
          src\MSOSync.App\SignalR\OperationChangedEvent.cs `
          src\MSOSync.App\SignalR\OperationChangedPublisher.cs `
          src\MSOSync.App\SignalR\OperationsEventType.cs `
          src\MSOSync.Metadata\MetadataServiceExtensions.cs `
          tests\MSOSync.MetadataTests\Operations\
  git commit -m "feat(12C-3): IOperationService, handlers, query service, SignalR publisher + unit tests"
  ```

---

## Acceptance criteria

- `dotnet build` passes for `MSOSync.Metadata`, `MSOSync.App`, and `MSOSync.MetadataTests`.
- All 12 unit tests pass (6 `OperationServiceTests` + 6 `OperationQueryServiceTests`).
- `IOperationService` is resolvable from DI.
- `IOperationHandler` for `Export`, `Rollout`, and `Decommission` are resolvable by key.
- `OperationChangedEvent` is published after every `CreateAsync`, `CompleteAsync`, `CancelAsync`, and `RetryAsync` call.
- `OperationChangedPublisher.Handle` sends to SignalR group `"operators"` with `operationId`, `operationType`, and `status` fields.
- Cursor pagination round-trips: page 1 returns `NextCursor`; page 2 with that cursor returns remaining items with `NextCursor = null`.
