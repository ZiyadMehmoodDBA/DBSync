# Epic 6: Transport Layer Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this design task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the full node-to-node transport layer — PULL and PUSH modes, batch delivery, sequence verification, acknowledgement, and apply stub — replacing the `NoOpTransportService` from Epic 5.

**Architecture:** `SmartTransportService` (in `MSOSync.Transport`) implements `ITransportService` and dispatches per-target based on `SyncNode.TransportMode` from the metadata cache. All sync endpoints are always active; `PullJob` self-disables if the local node is in PUSH mode. Apply is stubbed with `NoOpApplyService`; Epic 7 replaces that single registration with real SQL execution.

**Tech Stack:** C# 13 · .NET 9 · ASP.NET Core · EF Core 9 · `Microsoft.Extensions.Http` · Polly 8 · `System.IO.Compression` · xUnit 2.9.3 · FluentAssertions 6.12.2 · Moq 4.20.72 · Testcontainers.MsSql 4.4.0

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`, `LangVersion = 13.0`
- EF Core 9.0.0 — no raw SQL except where EF LINQ cannot express the query
- All `DateTime` via `IClock.UtcNow`; all `DateTimeOffset` for wire protocol fields (AckTime)
- `MSOSync.Transport` must NOT inject `AppDbContext` directly — use interfaces from Batch/Metadata
- `NodeToken` must NEVER appear in logs, HTTP responses, config dumps, or `appsettings.json`
- Transport endpoints at `/api/v1/sync/*` are guarded by `NodeTokenAuthMiddleware` (no JWT)
- Node identity on inbound requests comes from `context.User` claim `"nodeId"` — never trust payload node IDs
- Unit tests use SQLite in-memory (`Microsoft.EntityFrameworkCore.Sqlite`), NOT EF InMemory provider
- Integration tests use Testcontainers.MsSql 4.4.0 (require Docker)

---

## 1. Architecture Overview

**Pipeline position:**

```
Trigger → Event → Batch → Transport (Epic 6) → Apply (Epic 7)
```

**Five moving parts:**

1. **M012 migration** — adds `transport_mode` to `sync_node`; adds `batch_sequence`, `source_node_id`, `received_time` to `sync_incoming_batch`
2. **`MSOSync.Transport` module** — `SmartTransportService`, `PushClient`, `PullClient`, `INodeHttpClient` (Polly), `AcknowledgementService`, `GzipCompressionService`, `IApplyService` / `NoOpApplyService`, `ITransportFailureClassifier`, `IBatchTransportQueryService`
3. **`SyncController`** (in `MSOSync.Api`) — four endpoints always active, guarded by `NodeTokenAuthMiddleware`
4. **`PullJob`** (in `MSOSync.Scheduler`) — self-disables for PUSH nodes; polls per-channel → per-source
5. **`ITopologyService`** (new interface in `MSOSync.Metadata`) — discovers source nodes for PullJob

**Dependency graph for `MSOSync.Transport`:**
```
MSOSync.Transport
  ├── MSOSync.Common       (IClock, NodeProperties)
  ├── MSOSync.Persistence  (entities)
  ├── MSOSync.Batch        (IBatchStateMachine, IBatchTransportQueryService)
  ├── MSOSync.Engine       (ITransportService interface)
  └── MSOSync.Metadata     (INodeMetadataService)
```

No circular dependencies. Engine does NOT reference Transport.

**`ITransportService` updated signature** (in `MSOSync.Engine`):
```csharp
public interface ITransportService
{
    Task SendBatchAsync(SyncOutgoingBatch batch, IReadOnlyList<SyncDataEvent> events, CancellationToken ct = default);
}
```
`SyncEngine.RunAsync` passes events at the call site (events are in memory after `eventReader.ReadAsync`).
`NoOpTransportService` is deleted from `MSOSync.Engine`. `AddSyncEngine()` no longer registers `ITransportService`; `AddTransportServices()` registers `SmartTransportService` as `ITransportService`.

---

## 2. Data Model (M012)

### Migration M012

```csharp
// Two ALTER TABLE statements:

// sync_node: add transport mode
migrationBuilder.AddColumn<byte>(
    name: "transport_mode",
    schema: "msosync",
    table: "sync_node",
    type: "tinyint",
    nullable: false,
    defaultValue: (byte)1);

migrationBuilder.Sql(
    "ALTER TABLE [msosync].[sync_node] ADD CONSTRAINT CK_sync_node_transport_mode " +
    "CHECK (transport_mode IN (1, 2))");

// sync_incoming_batch: add sequence, source, received time
migrationBuilder.AddColumn<long>(
    name: "batch_sequence", schema: "msosync", table: "sync_incoming_batch",
    nullable: false, defaultValue: 0L);

migrationBuilder.AddColumn<string>(
    name: "source_node_id", schema: "msosync", table: "sync_incoming_batch",
    type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "");

migrationBuilder.AddColumn<DateTime>(
    name: "received_time", schema: "msosync", table: "sync_incoming_batch",
    type: "datetime2(7)", nullable: false,
    defaultValueSql: "SYSUTCDATETIME()");

// FK: source_node_id → sync_node
migrationBuilder.AddForeignKey(
    name: "FK_sync_incoming_batch_source_node",
    schema: "msosync", table: "sync_incoming_batch",
    column: "source_node_id",
    principalSchema: "msosync", principalTable: "sync_node",
    principalColumn: "node_id",
    onDelete: ReferentialAction.Restrict);

// Index for O(log N) sequence lookup (technical debt: replace with sync_node_channel_state later)
migrationBuilder.CreateIndex(
    name: "IX_sync_incoming_batch_source_sequence",
    schema: "msosync", table: "sync_incoming_batch",
    columns: new[] { "source_node_id", "batch_sequence" });
```

### New enums (MSOSync.Persistence)

```csharp
public enum TransportMode : byte    { Pull = 1, Push = 2 }
public enum IncomingBatchStatus : byte { New = 0, Applying = 1, Applied = 2, Error = 3, PartialSuccess = 4 }
```

### Updated `BatchStatus` (MSOSync.Batch — BREAKING change from Epic 5)

```csharp
public enum BatchStatus : byte
{
    New          = 0,
    Sending      = 1,   // PUSH: HTTP call in-flight; on crash → Sending→Error in SchedulerRecovery
    Acknowledged = 2,
    Error        = 3,
    Retry        = 4
}
```

Valid transitions for `BatchStateMachine`:
- `New → Sending` (PUSH: before HTTP call)
- `New → Acknowledged` (PULL: ACK success)
- `New → Error` (PULL: negative ACK)
- `Sending → Acknowledged` (PUSH: success)
- `Sending → Error` (PUSH: failure / timeout)
- `Error → Retry`
- `Retry → Sending` (PUSH retry)
- `Retry → Acknowledged` (PULL retry → ack)
- `Retry → Error`

**Named methods replace generic `TransitionAsync`:**

```csharp
public interface IBatchStateMachine
{
    Task<bool> MoveToSendingAsync(long batchId, CancellationToken ct = default);
    Task<bool> MoveToAcknowledgedAsync(long batchId, DateTimeOffset ackTime, CancellationToken ct = default);
    Task<bool> MoveToErrorAsync(long batchId, CancellationToken ct = default);
    Task<bool> MoveToRetryAsync(long batchId, CancellationToken ct = default);
}
```

### Updated entity properties

`SyncNode`:
```csharp
public TransportMode TransportMode { get; set; } = TransportMode.Pull;
```

`SyncIncomingBatch`:
```csharp
public IncomingBatchStatus Status     { get; set; } = IncomingBatchStatus.New;
public long                BatchSequence { get; set; }
public string              SourceNodeId  { get; set; } = null!;
public DateTime            ReceivedTime  { get; set; }
```

### Updated `NodeDto` (MSOSync.Metadata)

Add `TransportMode TransportMode` property. JSON serializes as `"Pull"` / `"Push"`.

### `NodeProperties` (MSOSync.Common)

```csharp
public sealed class NodeProperties
{
    public string NodeId    { get; init; } = null!;
    public string GroupId   { get; init; } = null!;
    public string SyncUrl   { get; init; } = null!;
    [JsonIgnore]
    public string NodeToken { get; init; } = null!;
}
```

Registered via `services.Configure<NodeProperties>()`. Env var mapping:
- `MSOSYNC_NODE_ID` → `Node:Id`
- `MSOSYNC_NODE_GROUP` → `Node:GroupId`
- `MSOSYNC_NODE_URL` → `Node:SyncUrl`
- `MSOSYNC_NODE_TOKEN` → `Node:NodeToken`  ← never logged, never serialized

---

## 3. Wire Format DTOs (MSOSync.Transport)

All records in `MSOSync.Transport` namespace. JSON source generation (`JsonSerializerContext`) for performance.

```csharp
// Event within a batch
// EventType: 'I' → "INSERT", 'U' → "UPDATE", 'D' → "DELETE" (mapped from SyncDataEvent.EventType char)
public sealed record EventPayload(
    long    EventId,
    string  TriggerId,
    string  EventType,      // "INSERT" | "UPDATE" | "DELETE"
    string  TableName,
    long?   TransactionId,
    string? PkData,
    string? RowData);

// The batch on the wire — Events list; entire HTTP body gzip-compressed
public sealed record BatchPayload(
    long                       BatchId,
    long                       BatchSequence,
    string                     ChannelId,
    string                     SourceNodeId,
    string                     TargetNodeId,
    int                        RowCount,
    IReadOnlyList<EventPayload> Events);

// Pull request: target → source
public sealed record PullRequest(
    string TargetNodeId,
    string ChannelId,
    long   AfterSequence);

// Pull response: source → target
public sealed record PullResponse(
    IReadOnlyList<BatchPayload> Batches,
    bool                        MoreAvailable);

// ACK: target → source (PULL mode; PUSH uses PushResponse inline)
public sealed record AckPayload(
    long            BatchId,
    long            BatchSequence,
    string          NodeId,
    bool            Success,
    string?         ErrorMessage,
    DateTimeOffset  AckTime);

// Push response: target → source (PUSH mode)
public sealed record PushResponse(
    long    BatchId,
    bool    Success,
    int     AppliedRows,
    int     ErrorRows,
    string? ErrorMessage);

// Ping response
public sealed record PingResponse(
    string        NodeId,
    NodeStatus    Status,
    TransportMode TransportMode);
```

---

## 4. Transport Flows

### PUSH (source initiates)

```
SyncEngine.RunAsync() calls ITransportService.SendBatchAsync(batch, events)
  ↓
SmartTransportService:
  node = INodeMetadataService.GetNodeAsync(batch.NodeId)
  node == null || !node.SyncEnabled → log + increment msosync_transport_skipped_total → return
  node.TransportMode == Pull → no-op, return (batch stays New)
  ↓
  IBatchStateMachine.MoveToSendingAsync(batchId)
  Build BatchPayload(batch + events.Select(EventPayload))
  ↓
PushClient.PushAsync(node.SyncUrl, payload, ct)
  → INodeHttpClient: gzip body, X-Node-Id, X-Node-Token, X-Correlation-Id
  → POST {targetUrl}/api/v1/sync/push
  ← PushResponse
  ↓
  success → AcknowledgementService.AcknowledgeAsync(batchId, success:true, ackTime)
            IBatchStateMachine.MoveToAcknowledgedAsync → set AckTime
  failure → AcknowledgementService.AcknowledgeAsync(batchId, success:false, ...)
            ITransportFailureClassifier.Classify(ex) → TransportFailureReason
            insert SyncBatchError; IBatchStateMachine.MoveToErrorAsync
```

### PULL — source side (`POST /api/v1/sync/pull`)

```
PullRequest{TargetNodeId, ChannelId, AfterSequence}
  ↓
Validate: TargetNodeId == nodeId claim (401 if mismatch)
  ↓
IBatchTransportQueryService.GetNextPullBatchAsync(TargetNodeId, ChannelId, AfterSequence)
  None → 204 No Content
  ↓
IBatchTransportQueryService.GetEventsForBatchAsync(batchId)
Build BatchPayload; check has more → set MoreAvailable = Take(2).Count > 1
Batch status stays New (no transition here — source stays New until ACK)
  ↓
GzipCompressionService.Compress(JSON(payload))
Return 200 + PullResponse (Content-Encoding: gzip)
```

### PULL — target side (PullJob → PullClient)

```
PullJob tick → channels ordered by Priority DESC → for each source node:
  PullClient.PullAsync(source.SyncUrl, PullRequest{localNodeId, channelId, lastSeq})
  ← 204 → continue (increment msosync_pull_empty_total)
  ← PullResponse
  ↓
  for each batch:
    GzipCompressionService.Decompress → BatchPayload
    Sequence check: lastSeq + 1 == batch.BatchSequence?
      Gap → POST /sync/ack with Success=false, ErrorMessage="SEQUENCE_GAP"
            insert SyncBatchError(ConflictType=SequenceGap) → continue
    Duplicate check: (source_node_id, batch_sequence) exists in sync_incoming_batch?
      Yes → POST /sync/ack with Success=true (idempotent) → continue
    IBatchTransportQueryService.InsertIncomingBatchAsync → Status=New, ReceivedTime=now
    IApplyService.ApplyAsync → New→Applying→Applied/Error → ApplyResult
    POST /sync/ack with AckPayload{..., Success=applyResult.Success, AckTime=now}
  ↓
  MoreAvailable → immediately re-poll (skip timer wait)
```

### ACK processing (source — `POST /api/v1/sync/ack`)

```
AckPayload{BatchId, BatchSequence, NodeId, Success, ErrorMessage, AckTime}
  ↓
AcknowledgementService.AcknowledgeAsync:
  load batch; not found → 404
  already Acknowledged → 200 (idempotent)
  Success=true  → IBatchStateMachine.MoveToAcknowledgedAsync(ackTime)
  Success=false → IBatchStateMachine.MoveToErrorAsync
                  insert SyncBatchError(reason from ErrorMessage)
```

**SchedulerRecovery addition:** on startup, find batches with `status=Sending AND sent_time < now - 5 min` → `MoveToErrorAsync` (stale Sending = process crash during push).

---

## 5. Transport Module Components

### `GzipCompressionService` (singleton)

```csharp
byte[] Compress(byte[] data);   // GZipStream CompressionLevel.Optimal
byte[] Decompress(byte[] data);
```

Replaces `GzipBatchCompressor` (deleted from `MSOSync.Batch`).

### `TransportFailureReason` enum

```csharp
public enum TransportFailureReason
{
    Timeout, HttpError, ConnectionRefused,
    CompressionFailure, SequenceGap, ApplyFailure, Unknown
}
```

### `ITransportFailureClassifier`

```csharp
public interface ITransportFailureClassifier
{
    TransportFailureReason Classify(Exception ex);
}
```

Mappings: `TaskCanceledException/OperationCanceledException(timeout)→Timeout`, `HttpRequestException→ConnectionRefused`, `InvalidDataException→CompressionFailure`, `JsonException→CompressionFailure`, else `Unknown`.

### `INodeHttpClient` + `NodeHttpClient`

```csharp
public interface INodeHttpClient
{
    Task<T>  PostAsync<TReq, T>(string url, TReq body, string nodeId, string nodeToken, CancellationToken ct);
    Task<T?> PostNullableAsync<TReq, T>(string url, TReq body, string nodeId, string nodeToken, CancellationToken ct);
}
```

Sets: `X-Node-Id`, `X-Node-Token`, `X-Correlation-Id` (from `IHttpContextAccessor` or generated), `Content-Encoding: gzip`, `Accept-Encoding: gzip`.

Polly (via `AddHttpClient`):
- Retry: 3 attempts, delays 1s / 2s / 5s, on transient HTTP errors
- Circuit breaker: 5 consecutive failures → open 30s

### `SmartTransportService : ITransportService`

Reads `INodeMetadataService` (cache, 60s TTL). Dispatches PUSH or no-op. Logs skips. Increments `msosync_transport_skipped_total`.

### `PushClient`

Uses `INodeHttpClient`. POST to `{syncUrl}/api/v1/sync/push`. Returns `PushResponse`.

### `PullClient`

Uses `INodeHttpClient`. POST to `{syncUrl}/api/v1/sync/pull`. Handles 204 as null. Returns `PullResponse?`.

### `AcknowledgementService`

Uses `IBatchStateMachine` (named methods only — never direct entity mutation). Idempotent: already-Acknowledged → return. Inserts `SyncBatchError` on failure with `TransportFailureReason`.

### `IApplyService` / `NoOpApplyService`

```csharp
public interface IApplyService
{
    Task<ApplyResult> ApplyAsync(SyncIncomingBatch incoming, BatchPayload payload, CancellationToken ct);
}

public sealed record ApplyResult(bool Success, int AppliedRows, int ErrorRows, string? ErrorMessage);
```

`NoOpApplyService`: transitions `New→Applying→Applied`, returns `ApplyResult(true, payload.RowCount, 0, null)`.

### `IBatchTransportQueryService`

```csharp
public interface IBatchTransportQueryService
{
    Task<(SyncOutgoingBatch? Batch, bool MoreAvailable)> GetNextPullBatchAsync(
        string targetNodeId, string channelId, long afterSequence, CancellationToken ct);
    Task<IReadOnlyList<SyncDataEvent>> GetEventsForBatchAsync(long batchId, CancellationToken ct);
    Task<long> GetLastSequenceAsync(string sourceNodeId, string channelId, CancellationToken ct);
    Task<bool> IncomingBatchExistsAsync(string sourceNodeId, long batchSequence, CancellationToken ct);
    Task InsertIncomingBatchAsync(SyncIncomingBatch batch, CancellationToken ct);
}
```

Implementation (`BatchTransportQueryService`) uses `AppDbContext` (scoped). Lives in `MSOSync.Transport` — this is the only class in Transport that touches EF directly.

### `TransportServiceExtensions.AddTransportServices()`

Registers: `GzipCompressionService` (singleton), `INodeHttpClient`/`NodeHttpClient` (typed HttpClient + Polly), `SmartTransportService` as `ITransportService` (scoped), `PushClient` (scoped), `PullClient` (scoped), `AcknowledgementService` (scoped), `IApplyService`/`NoOpApplyService` (scoped), `ITransportFailureClassifier`/`TransportFailureClassifier` (singleton), `IBatchTransportQueryService`/`BatchTransportQueryService` (scoped).

---

## 6. SyncController (MSOSync.Api)

`[Route("api/v1/sync")]` — no `[Authorize]`; `NodeTokenAuthMiddleware` guards the path.

**Injects:** `IBatchTransportQueryService`, `AcknowledgementService`, `IApplyService`, `GzipCompressionService`, `IOptions<NodeProperties>`, `INodeMetadataService`, `IClock`

### `POST /pull`

1. Validate `req.TargetNodeId == nodeId claim` → 401 if mismatch
2. `GetNextPullBatchAsync` → 204 if none
3. `GetEventsForBatchAsync`
4. Build `BatchPayload`; batch status unchanged (stays New)
5. Compress + return `PullResponse(Batches=[payload], MoreAvailable)` with `Content-Encoding: gzip`

### `POST /push`

1. Decompress request body → `BatchPayload`
2. Validate `payload.TargetNodeId == nodeId claim` → 401 if mismatch
3. `IncomingBatchExistsAsync(sourceNodeId, batchSequence)` → already exists → 200 `PushResponse(Success=true)` (idempotent)
4. Sequence check: `GetLastSequenceAsync(sourceNodeId, channelId) + 1 == batchSequence` → mismatch → insert `SyncBatchError(SequenceGap)` → 409 `{"code":"SEQUENCE_GAP"}`
5. `InsertIncomingBatchAsync(Status=New, ...)`
6. `IApplyService.ApplyAsync` → `ApplyResult`
7. Return 200 `PushResponse { BatchId, Success, AppliedRows, ErrorRows }`

### `POST /ack`

1. `AcknowledgementService.AcknowledgeAsync(payload)`
2. 200 OK (or 404 if batchId not found, 200 if already Acknowledged)

### `POST /ping`

```csharp
var own = await _nodeMetadata.GetNodeAsync(_props.NodeId, ct);
return Ok(new PingResponse(_props.NodeId, NodeStatus.Active, own?.TransportMode ?? TransportMode.Pull));
```

---

## 7. PullJob (MSOSync.Scheduler)

```csharp
public sealed class PullJob(
    IServiceScopeFactory scopeFactory,
    IOptions<NodeProperties> props,
    IConfiguration config,
    ILogger<PullJob> logger) : BackgroundService
```

```
ExecuteAsync:
  Resolve own node → TransportMode == Push → log "PullJob disabled (Push mode)" → return
  
  interval = config.GetValue<int>("Sync:PullIntervalSeconds", 10) seconds
  using timer = new PeriodicTimer(interval)
  
  while timer.WaitForNextTickAsync(ct):
    channels = IChannelMetadataService.GetChannelsAsync() ordered by Priority DESC
    sources  = ITopologyService.GetSourceNodes(props.NodeId, ct)
    
    foreach channel (high priority first):
      foreach source:
        lastSeq = IBatchTransportQueryService.GetLastSequenceAsync(source.NodeId, channel.ChannelId)
        
        poll:
          response = PullClient.PullAsync(source.SyncUrl, PullRequest{...}, ct)
          204 → increment msosync_pull_empty_total; continue
          
          start = clock.UtcNow
          foreach batch in response.Batches:
            [sequence + duplicate checks]
            InsertIncomingBatchAsync
            IApplyService.ApplyAsync
            POST /ack
          msosync_pull_batches_total += response.Batches.Count
          msosync_pull_duration_seconds.Record(elapsed)
          
          if response.MoreAvailable → goto poll (immediate re-poll)
        
        msosync_pull_requests_total++
    
    catch Exception when !ct.IsCancellationRequested → LogError
```

**`ITopologyService`** (new interface in `MSOSync.Metadata`):

```csharp
public interface ITopologyService
{
    Task<IReadOnlyList<NodeDto>> GetSourceNodes(string localNodeId, CancellationToken ct = default);
}
```

Epic 6 implementation: returns all `Active` nodes where `NodeId != localNodeId`, ordered by NodeId. CE assumption: one hub.

---

## 8. Program.cs Updates

```csharp
using MSOSync.Transport;
// ...
builder.Services.Configure<NodeProperties>(builder.Configuration.GetSection("Node"));
builder.Services.AddTransportServices(builder.Configuration);
```

`AddSyncEngine()` change: remove `services.AddScoped<ITransportService, NoOpTransportService>()` — `AddTransportServices` provides the real registration.

`AddSyncScheduler()` change: add `services.AddHostedService<PullJob>()`.

`MSOSync.App.csproj`: add `<ProjectReference Include="..\MSOSync.Transport\MSOSync.Transport.csproj" />`.

---

## 9. Testing

### New project: `MSOSync.TransportTests`

SQLite in-memory, same structure as `MSOSync.EngineTests`.

**Unit tests:**

| Class | Key cases |
|---|---|
| `SmartTransportServiceTests` | PUSH dispatches; PULL no-op; `SyncEnabled=false` skips; unknown node skips; skip increments counter |
| `AcknowledgementServiceTests` | success → Acknowledged; failure → Error + SyncBatchError; duplicate → 200 no-op |
| `GzipCompressionServiceTests` | compress/decompress round-trip; large payload; empty payload |
| `TransportFailureClassifierTests` | TimeoutException→Timeout; HttpRequestException→ConnectionRefused; JsonException→CompressionFailure |
| `SequenceVerificationTests` | first batch (seq=1, lastSeq=0) OK; gap (1,2,4) → SequenceGap; replay (seq already exists) → idempotent |

### Integration tests (`MSOSync.IntegrationTests/Transport/`)

Docker-gated (Testcontainers.MsSql). `WebApplicationFactory<Program>`.

**Mandatory tests:**

| Test | Validates |
|---|---|
| `DuplicatePush_Ignored` | Push batch_sequence=5 twice → second returns 200, one IncomingBatch row, one apply call |
| `DuplicateAck_Ignored` | POST /ack three times → batch stays Acknowledged, no exception |
| `Pull_NoBatch_Returns204` | No New/Retry batches → 204 No Content |
| `SchedulerRecovery_StaleSending_ToError` | Create Sending batch with sent_time=15 min ago; start service; verify Error status |

**Chaos test:**

| Test | Validates |
|---|---|
| `Push_AckLost_ReplayIgnored` | Push batch → apply succeeds → ACK times out (source retries) → duplicate push ignored → one IncomingBatch row |

---

## 10. Technical Debt

- **`sync_node_channel_state`** table (`source_node_id, channel_id, last_sequence`) — O(1) sequence lookup, replacing `MAX(batch_sequence)` query. Add in Epic 9 (Observability) or before load testing.
- **`ITransportStrategy` plugin pattern** — replace `if (mode == Push)` branch with strategy resolver. Add when streaming/gRPC transport needed (post-CE).
- **Unique constraint** on `sync_incoming_batch(source_node_id, batch_sequence)` — enforce at DB level once sequence tracking is stable.
- **`MSOSYNC_TRANSPORT_MODE` env var** — removed; transport mode is node metadata (DB), not deployment config.
