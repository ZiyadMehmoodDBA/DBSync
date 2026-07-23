# Task 4: Pipeline Optimization, Compression Gate, Metrics Instrumentation + Integration

> Part of [Phase 2D.5 Master Plan](2026-07-23-phase-2D-5-master.md)

**Goal:** Wire all pipeline optimizations: parallel batch dispatch in `SyncEngine`, compression threshold gate in `NodeHttpClient`, metrics instrumentation across all four pipeline stages, `appsettings.json` updates, and full-stack integration tests for compression gate and negotiator.

**Files:**
- Modify: `src/MSOSync.Engine/SyncEngine.cs`
- Modify: `src/MSOSync.Engine/SyncEngineExtensions.cs`
- Modify: `src/MSOSync.Transport/NodeHttpClient.cs`
- Modify: `src/MSOSync.Transport/AcknowledgementService.cs`
- Modify: `src/MSOSync.Transport/SmartTransportService.cs`
- Modify: `src/MSOSync.App/appsettings.json`
- Create: `tests/MSOSync.TransportTests/CompressionGateTests.cs`

**Interfaces:**
- Consumes (from Task 1):
  - `IMetricsService.RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null)`
  - `ICompressionNegotiator.SelectFor(string nodeId)` → `ICompressionService`
  - `CompressionOptions.ThresholdBytes`
- Consumes (from Task 2): nothing directly (orchestrator calls SyncJob, not SyncEngine)
- Produces: no new interfaces; all changes are internal wiring

---

- [ ] **Step 1: Update `SyncEngine` — parallel dispatch + metrics instrumentation**

`SyncEngine` currently dispatches batches serially in a `foreach`. After this change:
- Fetch stage is timed via `IMetricsService`
- Batches are grouped by `NodeId` and dispatched in parallel with `Task.WhenAll`
- Each node group gets its own `IServiceScope`

`SyncEngine` needs `IMetricsService` (from `MSOSync.Common`) and `IServiceScopeFactory`. Both are available via DI constructor injection.

Replace `src/MSOSync.Engine/SyncEngine.cs` entirely:

```csharp
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Event;
using MSOSync.Routing;
using MSOSync.Trigger;

namespace MSOSync.Engine;

public sealed class SyncEngine(
    ITriggerDriftDetector   driftDetector,
    IEventReader            eventReader,
    IRoutingService         routingService,
    IBatchCreator           batchCreator,
    ITransportService       transport,
    IServiceScopeFactory    scopeFactory,
    IMediator               mediator,
    IMetricsService         metrics,
    IClock                  clock,
    ILogger<SyncEngine>     logger)
{
    private const int BatchReadSize = 1000;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var start = clock.UtcNow;
        logger.LogDebug("SyncEngine.RunAsync starting");

        // 1. Drift detection — log only, never block
        try { await driftDetector.DetectAllAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Drift detection failed — continuing"); }

        // 2. Read unprocessed events (instrumented)
        var fetchSw = Stopwatch.StartNew();
        var events  = await eventReader.ReadAsync(BatchReadSize, ct);
        fetchSw.Stop();
        metrics.RecordHistogram("sync.pipeline.fetch_ms", fetchSw.Elapsed.TotalMilliseconds);

        if (events.Count == 0)
        {
            logger.LogDebug("SyncEngine: no events to process");
            await mediator.Publish(new SyncCycleCompletedEvent(0, 0, clock.UtcNow - start), ct);
            return;
        }

        // 3. Resolve routes for each event
        var routes = new Dictionary<long, IReadOnlyList<string>>();
        foreach (var evt in events)
            routes[evt.EventId] = await routingService.ResolveAsync(evt.TriggerId, ct);

        // 4. Create batches
        var batches = await batchCreator.CreateBatchesAsync(events, routes, ct);

        // 5. Parallel dispatch: group by NodeId, one IServiceScope per group
        //    Batches within a node group are dispatched serially (preserves sequence order per channel).
        //    Batches for different nodes are dispatched concurrently.
        var byNode = batches.GroupBy(b => b.NodeId).ToList();

        await Task.WhenAll(byNode.Select(group =>
            DispatchNodeBatchesAsync(group.Key, group.ToList(), events, ct)));

        // 6. Publish cycle event
        var duration = clock.UtcNow - start;
        logger.LogInformation("SyncEngine: read={Events} batches={Batches} elapsed={Elapsed}",
            events.Count, batches.Count, duration);
        await mediator.Publish(new SyncCycleCompletedEvent(events.Count, batches.Count, duration), ct);
    }

    /// <summary>
    /// Dispatches all batches for a single target node using a dedicated IServiceScope.
    /// Batches within the scope are sent serially to preserve per-channel sequence order.
    /// send_ms is instrumented per-batch inside SmartTransportService.SendBatchAsync.
    /// </summary>
    private async Task DispatchNodeBatchesAsync(
        string                           nodeId,
        IReadOnlyList<SyncOutgoingBatch> nodeBatches,
        IReadOnlyList<SyncDataEvent>     events,
        CancellationToken                ct)
    {
        await using var scope     = scopeFactory.CreateAsyncScope();
        var scopedTransport = scope.ServiceProvider.GetRequiredService<ITransportService>();

        foreach (var batch in nodeBatches)
            await scopedTransport.SendBatchAsync(batch, events, ct);
    }
}
```

- [ ] **Step 2: Update `SyncEngineExtensions` to register `IServiceScopeFactory` in `SyncEngine`**

`IServiceScopeFactory` is already registered by the .NET hosting infrastructure and is always available in DI. However, `SyncEngine` is registered as `AddScoped<SyncEngine>()` and the `IServiceScopeFactory` is available as a singleton — this is the standard .NET pattern for creating child scopes within a scoped service. No change to the extension method is required: constructor injection handles it automatically.

Verify that `SyncEngineExtensions.cs` remains as-is — its `AddScoped<SyncEngine>()` registration is correct. Read the file to confirm and do nothing if it already registers `IMetricsService` from `MSOSync.Common`.

**Action:** Register `IMetricsService` as singleton in `SyncEngineExtensions` so it is available when `SyncEngine` is resolved.

Replace `src/MSOSync.Engine/SyncEngineExtensions.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common;

namespace MSOSync.Engine;

public static class SyncEngineExtensions
{
    public static IServiceCollection AddSyncEngine(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SyncEngine>());

        // IMetricsService — singleton ring-buffer implementation (Phase 2F swaps in OpenTelemetry)
        services.AddSingleton<IMetricsService, InMemoryMetricsService>();

        // ITransportService registered by AddTransportServices() in MSOSync.Transport
        services.AddScoped<SyncEngine>();
        return services;
    }
}
```

- [ ] **Step 3: Update `NodeHttpClient` — add compression gate + ICompressionNegotiator**

`NodeHttpClient` currently compresses every outgoing payload unconditionally. After this change:
- Constructor takes `ICompressionNegotiator` + `IOptions<CompressionOptions>` instead of `GzipCompressionService`
- `SendAsync` checks payload size against `ThresholdBytes`; below threshold → raw send, no `Content-Encoding` header
- `ReadBodyAsync` decompresses using `gzip` or `br` based on `Content-Encoding` response header

Replace `src/MSOSync.Transport/NodeHttpClient.cs` entirely:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MSOSync.Common;

namespace MSOSync.Transport;

public sealed class NodeHttpClient(
    HttpClient                  httpClient,
    ICompressionNegotiator      negotiator,
    IOptions<CompressionOptions> compressionOptions,
    IMetricsService             metrics,
    IHttpContextAccessor?       httpContextAccessor = null) : INodeHttpClient
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(TransportJsonContext.Default.Options);

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        response.EnsureSuccessStatusCode();
        var json = await ReadBodyAsync(response, ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts)!;
    }

    public async Task<TResponse?> PostNullableAsync<TRequest, TResponse>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        response.EnsureSuccessStatusCode();
        var json = await ReadBodyAsync(response, ct);
        return JsonSerializer.Deserialize<TResponse>(json, JsonOpts);
    }

    public async Task PostVoidAsync<TRequest>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var response = await SendAsync(url, body, nodeId, nodeToken, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes    = await response.Content.ReadAsByteArrayAsync(ct);
        var encoding = response.Content.Headers.ContentEncoding;

        if (encoding.Contains("gzip"))
        {
            var gzip = new GzipCompressionService(compressionOptions);
            bytes = gzip.Decompress(bytes);
        }
        else if (encoding.Contains("br"))
        {
            var brotli = new BrotliCompressionService(compressionOptions);
            bytes = brotli.Decompress(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<HttpResponseMessage> SendAsync<TRequest>(
        string url, TRequest body, string nodeId, string nodeToken, CancellationToken ct)
    {
        var json      = JsonSerializer.Serialize(body, JsonOpts);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var threshold = compressionOptions.Value.ThresholdBytes;

        byte[] outBytes;
        string? contentEncoding = null;

        if (jsonBytes.Length >= threshold)
        {
            // Apply compression and record timing
            var compressionSvc = negotiator.SelectFor(nodeId);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            outBytes = compressionSvc.Compress(jsonBytes);
            sw.Stop();
            metrics.RecordHistogram(
                "sync.pipeline.compress_ms",
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, string> { ["node_id"] = nodeId, ["encoding"] = compressionSvc.EncodingName });
            contentEncoding = compressionSvc.EncodingName;
        }
        else
        {
            // Below threshold — send raw; no Content-Encoding header
            outBytes = jsonBytes;
        }

        var content = new ByteArrayContent(outBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (contentEncoding is not null)
            content.Headers.ContentEncoding.Add(contentEncoding);

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("X-Node-Id",       nodeId);
        request.Headers.Add("X-Node-Token",    nodeToken);
        request.Headers.Add("Accept-Encoding", "gzip, br");

        var correlationId = GetOrCreateCorrelationId();
        request.Headers.Add("X-Correlation-Id", correlationId);

        return await httpClient.SendAsync(request, ct);
    }

    private string GetOrCreateCorrelationId()
    {
        var ctx = httpContextAccessor?.HttpContext;
        if (ctx != null && ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var id))
            return id.ToString();
        return Guid.NewGuid().ToString("N");
    }
}
```

- [ ] **Step 4: Instrument `AcknowledgementService` with `ack_ms`**

`AcknowledgementService` needs `IMetricsService` for the `ack_ms` histogram. Modify `src/MSOSync.Transport/AcknowledgementService.cs` — add `IMetricsService metrics` constructor parameter and wrap `AcknowledgeOutgoingAsync` in a `Stopwatch`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

/// <summary>
/// Handles both outgoing ACK (PUSH mode — source side) and incoming ACK (PULL mode — from POST /ack).
/// </summary>
public sealed class AcknowledgementService(
    IBatchStateMachine              stateMachine,
    AppDbContext                    db,
    IMetricsService                 metrics,
    ILogger<AcknowledgementService> logger)
{
    /// <summary>
    /// Called by SmartTransportService after a PUSH attempt completes.
    /// </summary>
    public async Task AcknowledgeOutgoingAsync(
        long                    batchId,
        bool                    success,
        DateTimeOffset          ackTime,
        TransportFailureReason? reason,
        string?                 errorMessage,
        CancellationToken       ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (success)
            {
                await stateMachine.MoveToAcknowledgedAsync(batchId, ackTime, ct);
                logger.LogInformation("Batch {BatchId} acknowledged at {AckTime}", batchId, ackTime);
            }
            else
            {
                await stateMachine.MoveToErrorAsync(batchId, ct);
                if (errorMessage != null)
                {
                    db.BatchErrors.Add(new SyncBatchError
                    {
                        BatchId      = batchId,
                        ConflictType = (reason ?? TransportFailureReason.HttpError).ToString(),
                        ErrorMessage = errorMessage
                    });
                    await db.SaveChangesAsync(ct);
                }
                logger.LogWarning("Batch {BatchId} push failed reason={Reason}: {Error}",
                    batchId, reason, errorMessage);
            }
        }
        finally
        {
            sw.Stop();
            metrics.RecordHistogram(
                "sync.pipeline.ack_ms",
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, string>
                {
                    ["node_id"] = batchId.ToString(),
                    ["success"] = success.ToString()
                });
        }
    }

    /// <summary>
    /// Called by SyncController POST /ack — handles ACK from a PULL target.
    /// Returns false if batch not found.
    /// Idempotent: already-Acknowledged batch returns true (no-op).
    /// </summary>
    public async Task<bool> AcknowledgeIncomingAsync(
        AckPayload        payload,
        CancellationToken ct = default)
    {
        var batch = await db.OutgoingBatches.FindAsync([payload.BatchId], ct);
        if (batch == null) return false;

        if (batch.Status == (byte)BatchStatus.Acknowledged)
        {
            logger.LogDebug("Batch {BatchId} already acknowledged — ignoring duplicate ACK",
                payload.BatchId);
            return true;
        }

        if (payload.Success)
        {
            await stateMachine.MoveToAcknowledgedAsync(payload.BatchId, payload.AckTime, ct);
        }
        else
        {
            await stateMachine.MoveToErrorAsync(payload.BatchId, ct);
            db.BatchErrors.Add(new SyncBatchError
            {
                BatchId      = payload.BatchId,
                ConflictType = payload.ErrorCode?.StartsWith("SEQUENCE_GAP", StringComparison.Ordinal) == true
                    ? "SequenceGap"
                    : TransportFailureReason.ApplyFailure.ToString(),
                ErrorMessage = payload.ErrorCode
            });
            await db.SaveChangesAsync(ct);
        }

        return true;
    }
}
```

- [ ] **Step 5: Instrument `SmartTransportService` with `send_ms`**

`SmartTransportService.SendBatchAsync` is where per-batch send timing should be recorded (per spec: `sync.pipeline.send_ms` with tags `node_id`, `batch_id`). Add `IMetricsService` constructor parameter and wrap the `pushClient.PushAsync` call in a `Stopwatch`.

Replace `src/MSOSync.Transport/SmartTransportService.cs` entirely:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Transport;

public sealed class SmartTransportService(
    INodeMetadataService           nodeMetadata,
    PushClient                     pushClient,
    IBatchStateMachine             stateMachine,
    AcknowledgementService         acknowledgement,
    ITransportFailureClassifier    classifier,
    IMetricsService                metrics,
    IClock                         clock,
    ILogger<SmartTransportService> logger) : ITransportService
{
    public async Task SendBatchAsync(
        SyncOutgoingBatch            batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken            ct = default)
    {
        var node = await nodeMetadata.GetNodeAsync(batch.NodeId, ct);

        if (node == null)
        {
            logger.LogWarning("Transport: node {NodeId} not found — skipping batch {BatchId}",
                batch.NodeId, batch.BatchId);
            return;
        }

        if (!node.CanSynchronize)
        {
            logger.LogDebug("Transport: node {NodeId} not sync-eligible (lifecycle={Lifecycle}, maintenance={Maint}) — skipping batch {BatchId}",
                batch.NodeId, node.LifecycleState, node.MaintenanceMode, batch.BatchId);
            return;
        }

        if (node.TransportMode == TransportMode.Pull)
        {
            logger.LogDebug("Transport: node {NodeId} is Pull — batch {BatchId} awaits pull",
                batch.NodeId, batch.BatchId);
            return;
        }

        await stateMachine.MoveToSendingAsync(batch.BatchId, ct);

        var sw = Stopwatch.StartNew();
        try
        {
            var result  = await pushClient.PushAsync(node.SyncUrl, batch, events, ct);
            sw.Stop();
            metrics.RecordHistogram(
                "sync.pipeline.send_ms",
                sw.Elapsed.TotalMilliseconds,
                new Dictionary<string, string>
                {
                    ["node_id"]  = batch.NodeId,
                    ["batch_id"] = batch.BatchId.ToString()
                });

            var ackTime = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, result.Success, ackTime, null, result.ErrorMessage, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var reason  = classifier.Classify(ex);
            var ackTime = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
            logger.LogError(ex, "Transport: push failed for batch {BatchId} — reason={Reason}",
                batch.BatchId, reason);
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, false, ackTime, reason, ex.Message, ct);
        }
    }
}
```

- [ ] **Step 6: Add `appsettings.json` configuration sections**

Add the two new sections to `src/MSOSync.App/appsettings.json`. The existing `"Sync"` section is retained unchanged:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "AllowedHosts": "*",
  "Node": {
    "NodeId": "",
    "GroupId": "",
    "SyncUrl": ""
  },
  "Jwt": {
    "Issuer": "msosync",
    "Audience": "msosync-dashboard",
    "AccessExpiryMinutes": 60,
    "RefreshExpiryDays": 7
  },
  "Heartbeat": {
    "IntervalSeconds": 30,
    "ProbeIntervalSeconds": 60,
    "StatusCheckIntervalSeconds": 60,
    "MissedThreshold": 3
  },
  "Sync": {
    "IntervalSeconds": 30,
    "PullIntervalSeconds": 10
  },
  "AdaptivePolling": {
    "MinIntervalSeconds": 5,
    "MaxIntervalSeconds": 300,
    "BaseIntervalSeconds": 30,
    "BackoffMultiplier": 2.0,
    "ErrorBackoffMultiplier": 2.0,
    "MaxErrorBackoffCount": 5,
    "ErrorJitterFraction": 0.20,
    "BusyThresholdCycles": 3,
    "IdleThresholdCycles": 2,
    "ActivityWindowMinutes": 60
  },
  "Compression": {
    "ThresholdBytes": 1024,
    "Level": "Fastest"
  },
  "Export": {
    "ImmediateThreshold": 50000,
    "BasePath": "exports",
    "RetentionHours": 24,
    "MaxConcurrentJobs": 1
  },
  "Pagination": {
    "CursorHmacKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
  },
  "Lifecycle": {
    "RollingWorkerIntervalSeconds": 15
  },
  "Replay": {
    "MaxRangeDays": 90,
    "WorkerIntervalSeconds": 10,
    "MaxConcurrentOperations": 5,
    "ItemPageSize": 50
  }
}
```

- [ ] **Step 7: Write compression gate tests**

Create `tests/MSOSync.TransportTests/CompressionGateTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

/// <summary>
/// Tests that NodeHttpClient applies or skips compression based on ThresholdBytes.
/// Uses a captured-request HttpMessageHandler to inspect the outgoing request.
/// </summary>
public sealed class CompressionGateTests : IDisposable
{
    private HttpRequestMessage? _capturedRequest;
    private byte[]? _capturedBody;

    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private NodeHttpClient BuildClient(int thresholdBytes)
    {
        var opts = Options.Create(new CompressionOptions { ThresholdBytes = thresholdBytes, Level = CompressionLevelOption.Fastest });

        var gzip   = new GzipCompressionService(opts);
        var brotli = new BrotliCompressionService(opts);
        var negotiator = new CompressionNegotiator(_cache, gzip, brotli);
        var metrics = Mock.Of<IMetricsService>();

        var handler = new CapturingHandler(req =>
        {
            _capturedRequest = req;
            _capturedBody    = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake-node/") };
        return new NodeHttpClient(httpClient, negotiator, opts, metrics);
    }

    [Fact]
    public async Task BelowThreshold_NoContentEncodingHeader()
    {
        var client  = BuildClient(thresholdBytes: 1024);
        var payload = new string('x', 512); // 512 bytes < 1024 threshold

        // PostVoidAsync will throw (handler returns nothing), but we only need the captured request
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-1", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().BeEmpty();
    }

    [Fact]
    public async Task AboveThreshold_GzipContentEncodingHeader()
    {
        var client  = BuildClient(thresholdBytes: 1024);
        var payload = new string('x', 2048); // 2048 bytes > 1024 threshold (after JSON serialisation ~2052 bytes)

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-1", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().Contain("gzip");
    }

    [Fact]
    public async Task AtThreshold_CompressionIsApplied()
    {
        // Threshold is inclusive lower bound — payload at exactly threshold bytes triggers compression
        // JSON serialisation adds surrounding quotes: 1022 chars + 2 = 1024 bytes
        var client  = BuildClient(thresholdBytes: 1024);
        var payload = new string('y', 1022); // "\"yyyy...\"" = 1024 bytes in JSON

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-1", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().Contain("gzip");
    }

    [Fact]
    public async Task AboveThreshold_BrotliNodeCapability_UsesBrotliEncoding()
    {
        // Register brotli capability in cache for node-br
        _cache.Set("node-compression:node-br", new[] { "gzip", "br" });
        var client  = BuildClient(thresholdBytes: 64);
        var payload = new string('z', 100); // 100 bytes > 64 threshold

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostVoidAsync<string>("http://fake-node/api/v1/sync/push", payload, "node-br", "tok", CancellationToken.None));

        _capturedRequest!.Content!.Headers.ContentEncoding.Should().Contain("br");
    }

    public void Dispose() => _cache.Dispose();

    // -----------------------------------------------------------------------
    // Minimal capturing HttpMessageHandler
    // -----------------------------------------------------------------------

    private sealed class CapturingHandler(Action<HttpRequestMessage> capture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request);
            // Return 500 so the client's EnsureSuccessStatusCode throws — that's fine for our tests
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
```

- [ ] **Step 8: Run all transport tests**

```bash
dotnet test tests/MSOSync.TransportTests/MSOSync.TransportTests.csproj -v m
```

Expected: all tests PASS — including pre-existing tests (`AcknowledgementServiceTests`, `SmartTransportServiceTests`, etc.) and the new `CompressionGateTests`.

Note: `AcknowledgementServiceTests` constructs `AcknowledgementService` directly. Check that file now needs `IMetricsService` added to its test constructor. If tests fail due to the new `metrics` parameter, update the test file's `AcknowledgementService` construction to inject `Mock.Of<IMetricsService>()`.

- [ ] **Step 9: Fix `AcknowledgementServiceTests` and `SmartTransportServiceTests` if needed**

Read `tests/MSOSync.TransportTests/AcknowledgementServiceTests.cs` and check how `AcknowledgementService` is constructed. If it passes a direct constructor call, add `Mock.Of<IMetricsService>()` as the third parameter.

The construction pattern will look like:

```csharp
// Before (3 params):
var svc = new AcknowledgementService(stateMachine.Object, db, logger);

// After (4 params — add IMetricsService):
var svc = new AcknowledgementService(stateMachine.Object, db, Mock.Of<IMetricsService>(), logger);
```

Apply the fix to every `new AcknowledgementService(...)` call in that file.

Also check `tests/MSOSync.TransportTests/SmartTransportServiceTests.cs`. If it constructs `SmartTransportService` directly, add `Mock.Of<IMetricsService>()` as the new sixth parameter (before `clock`).

- [ ] **Step 10: Run engine tests to verify no regressions**

```bash
dotnet test tests/MSOSync.EngineTests/MSOSync.EngineTests.csproj -v m
```

Expected: all tests PASS. `SyncEngine` now has two additional constructor parameters (`IServiceScopeFactory`, `IMetricsService`). Tests that build `SyncEngine` directly must be updated to pass those. Locate all `new SyncEngine(...)` calls in `tests/MSOSync.EngineTests/` and add:
- `IServiceScopeFactory` — use `Mock.Of<IServiceScopeFactory>()` or build a real `ServiceProvider` with `services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()`
- `IMetricsService` — use `Mock.Of<IMetricsService>()` or `new InMemoryMetricsService()`

Apply the fix, then re-run:

```bash
dotnet test tests/MSOSync.EngineTests/MSOSync.EngineTests.csproj -v m
```

Expected: all tests PASS.

- [ ] **Step 11: Run full solution build**

```bash
dotnet build MSOSync.sln -c Debug
```

Expected: 0 errors, warnings may appear but should not include CS8604 (nullable) or CS0246 (type not found).

- [ ] **Step 12: Run entire test suite**

```bash
dotnet test MSOSync.sln -v m --logger "console;verbosity=minimal"
```

Expected: all test projects pass. Watch for:
- `MSOSync.SchedulerTests` — `SyncJobTests` (updated), `AdaptivePollingServiceTests` (new)
- `MSOSync.TransportTests` — `GzipCompressionServiceTests` (updated), `CompressionServiceTests` (new), `CompressionNegotiatorTests` (new), `CompressionGateTests` (new)
- `MSOSync.EngineTests` — all existing tests (updated constructors)
- All other projects — no changes expected

- [ ] **Step 13: Commit pipeline optimization and integration**

```bash
git add src/MSOSync.Engine/SyncEngine.cs \
        src/MSOSync.Engine/SyncEngineExtensions.cs \
        src/MSOSync.Transport/NodeHttpClient.cs \
        src/MSOSync.Transport/AcknowledgementService.cs \
        src/MSOSync.Transport/SmartTransportService.cs \
        src/MSOSync.App/appsettings.json \
        tests/MSOSync.TransportTests/CompressionGateTests.cs
git commit -m "feat(2D.5-T4): parallel channel dispatch, compression gate, metrics instrumentation, appsettings"
```

---

## Verification Checklist

After all four tasks are complete, verify the following before considering 2D.5 done:

- [ ] `dotnet build MSOSync.sln -c Release` — 0 errors
- [ ] `dotnet test MSOSync.sln` — all tests green
- [ ] `appsettings.json` contains both `"AdaptivePolling"` and `"Compression"` sections
- [ ] `SyncJob` no longer inherits `BackgroundService`; it is `AddScoped<SyncJob>()` in DI
- [ ] `AdaptivePollingOrchestrator` is registered via `AddHostedService<AdaptivePollingOrchestrator>()`
- [ ] `IWorkerStatusRegistry.Register(...)` is called in `AdaptivePollingOrchestrator.StartAsync`, NOT in `SyncJob`
- [ ] `PullJob` is untouched — still has `PeriodicTimer` with `PullIntervalSeconds`
- [ ] No new EF migrations exist
- [ ] `NodeHttpClient` ctor no longer references `GzipCompressionService` directly
- [ ] `CompressionGateTests` — below-threshold test asserts no `Content-Encoding` header
- [ ] `AdaptivePollingServiceTests` — idle backoff test verifies interval sequence 30→60→120→240→300
