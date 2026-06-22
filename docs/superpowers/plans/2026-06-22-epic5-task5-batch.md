# Task 5: MSOSync.Batch

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Batch/BatchStatus.cs`
- Create: `src/MSOSync.Batch/OutgoingBatchDto.cs`
- Create: `src/MSOSync.Batch/IBatchStateMachine.cs`
- Create: `src/MSOSync.Batch/BatchStateMachine.cs`
- Create: `src/MSOSync.Batch/IBatchCreator.cs`
- Create: `src/MSOSync.Batch/BatchCreator.cs`
- Create: `src/MSOSync.Batch/GzipBatchCompressor.cs`
- Create: `src/MSOSync.Batch/RetryProcessor.cs`
- Create: `src/MSOSync.Batch/BatchPurger.cs`
- Create: `src/MSOSync.Batch/BatchPipelineExtensions.cs`
- Delete: `src/MSOSync.Batch/Placeholder.cs`

**Interfaces:**
- Consumes:
  - `SyncOutgoingBatch` entity: `BatchId(long, PK), BatchSequence(long), NodeId(varchar50), ChannelId(varchar50), Status(byte/tinyint), RowCount(int), ByteCount(long), RetryCount(int), NextRetryTime(datetime2?), CreateTime(datetime2?), SentTime(datetime2?), AckTime(datetime2?)`
  - `SyncDataEvent` entity (EventId, TriggerId, ChannelId, TransactionId, RowData, IsProcessed)
  - `SyncDataEventBatch` entity (EventId, BatchId) — composite PK
  - `SyncChannel` entity (ChannelId, MaxBatchToSend, MaxDataSize)
  - `IClock` from Task 1
- Produces:
  - `BatchStatus` enum: `New=0, Sent=1, Ok=2, Error=3, Retry=4`
  - `OutgoingBatchDto` record
  - `IBatchStateMachine`: `CanTransition`, `TransitionAsync`
  - `IBatchCreator.CreateBatchesAsync(events, routes, ct)` → `IReadOnlyList<SyncOutgoingBatch>`
  - `GzipBatchCompressor`: `Compress/Decompress`
  - `RetryProcessor.ProcessAsync(ct)` → `int`
  - `BatchPurger.PurgeAsync(ct)` → `int`
  - `AddBatchPipeline(IServiceCollection, IConfiguration)` extension

**Note on status storage:** `SyncOutgoingBatch.Status` is `byte` (tinyint). `BatchStateMachine` casts `(byte)from`/`(byte)to` for SQL parameters.

---

- [ ] **Step 1: Create `BatchStatus`**

```csharp
// src/MSOSync.Batch/BatchStatus.cs
namespace MSOSync.Batch;

public enum BatchStatus : byte
{
    New   = 0,
    Sent  = 1,
    Ok    = 2,
    Error = 3,
    Retry = 4
}
```

- [ ] **Step 2: Create `OutgoingBatchDto`**

```csharp
// src/MSOSync.Batch/OutgoingBatchDto.cs
namespace MSOSync.Batch;

public sealed record OutgoingBatchDto(
    long BatchId,
    BatchStatus Status,
    string TargetNodeId,
    string ChannelId,
    DateTime? CreateTime,
    DateTime? SentTime,
    DateTime? AckTime,
    int RetryCount,
    int EventCount,
    string? ErrorMessage);
```

- [ ] **Step 3: Create `IBatchStateMachine`**

```csharp
// src/MSOSync.Batch/IBatchStateMachine.cs
namespace MSOSync.Batch;

public interface IBatchStateMachine
{
    bool CanTransition(BatchStatus from, BatchStatus to);
    Task<bool> TransitionAsync(long batchId, BatchStatus from, BatchStatus to, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `BatchStateMachine`**

```csharp
// src/MSOSync.Batch/BatchStateMachine.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class BatchStateMachine(AppDbContext db) : IBatchStateMachine
{
    private static readonly HashSet<(BatchStatus From, BatchStatus To)> ValidTransitions =
    [
        (BatchStatus.New,   BatchStatus.Sent),
        (BatchStatus.Sent,  BatchStatus.Ok),
        (BatchStatus.Sent,  BatchStatus.Error),
        (BatchStatus.Error, BatchStatus.Retry),
        (BatchStatus.Retry, BatchStatus.Sent),
        (BatchStatus.Retry, BatchStatus.Error),
    ];

    public bool CanTransition(BatchStatus from, BatchStatus to) =>
        ValidTransitions.Contains((from, to));

    public async Task<bool> TransitionAsync(
        long batchId, BatchStatus from, BatchStatus to, CancellationToken ct = default)
    {
        if (!CanTransition(from, to)) return false;

        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId && b.Status == (byte)from)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, (byte)to), ct);

        return rows == 1;
    }
}
```

- [ ] **Step 5: Create `IBatchCreator`**

```csharp
// src/MSOSync.Batch/IBatchCreator.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public interface IBatchCreator
{
    Task<IReadOnlyList<SyncOutgoingBatch>> CreateBatchesAsync(
        IReadOnlyList<SyncDataEvent> events,
        IReadOnlyDictionary<long, IReadOnlyList<string>> routes,
        CancellationToken ct = default);
}
```

- [ ] **Step 6: Create `BatchCreator`**

Groups: `channelId → targetNodeId → transactionId`. Transaction boundary is never split. Limits per channel: `MaxBatchToSend` (row count) and `MaxDataSize` (cumulative byte count). All inserts + `is_processed=1` in one DB transaction.

```csharp
// src/MSOSync.Batch/BatchCreator.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public sealed class BatchCreator(AppDbContext db, IClock clock) : IBatchCreator
{
    public async Task<IReadOnlyList<SyncOutgoingBatch>> CreateBatchesAsync(
        IReadOnlyList<SyncDataEvent> events,
        IReadOnlyDictionary<long, IReadOnlyList<string>> routes,
        CancellationToken ct = default)
    {
        if (events.Count == 0) return [];

        var channelIds = events.Select(e => e.ChannelId).Distinct().ToList();
        var channels = await db.Channels.AsNoTracking()
            .Where(c => channelIds.Contains(c.ChannelId))
            .ToDictionaryAsync(c => c.ChannelId, ct);

        var maxSeq = await db.OutgoingBatches.AnyAsync(ct)
            ? await db.OutgoingBatches.MaxAsync(b => b.BatchSequence, ct)
            : 0L;

        // Expand events × target nodes
        var pairs = events
            .SelectMany(e => routes.TryGetValue(e.EventId, out var targets)
                ? targets.Select(t => (Event: e, TargetNodeId: t))
                : [])
            .GroupBy(x => (x.Event.ChannelId, x.TargetNodeId));

        var batchBuilds = new List<(SyncOutgoingBatch Batch, List<SyncDataEvent> Events)>();

        foreach (var group in pairs)
        {
            var channelId    = group.Key.ChannelId;
            var targetNodeId = group.Key.TargetNodeId;
            channels.TryGetValue(channelId, out var ch);
            var maxRows  = ch?.MaxBatchToSend ?? 10;
            var maxBytes = ch?.MaxDataSize    ?? 1048576L;

            // Group by transaction, ordered by first event
            var txGroups = group
                .GroupBy(x => x.Event.TransactionId)
                .OrderBy(g => g.Min(x => x.Event.EventId))
                .ToList();

            var currentEvents = new List<SyncDataEvent>();
            var currentBytes  = 0L;

            foreach (var txGroup in txGroups)
            {
                var txEvents = txGroup.Select(x => x.Event).OrderBy(e => e.EventId).ToList();
                var txBytes  = txEvents.Sum(e => (long)(e.RowData?.Length ?? 0) * 2);

                if (currentEvents.Count > 0 &&
                    (currentEvents.Count + txEvents.Count > maxRows ||
                     currentBytes + txBytes > maxBytes))
                {
                    batchBuilds.Add(MakeBuild(++maxSeq, targetNodeId, channelId, currentEvents, clock.UtcNow));
                    currentEvents = [];
                    currentBytes  = 0;
                }

                currentEvents.AddRange(txEvents);
                currentBytes += txBytes;
            }

            if (currentEvents.Count > 0)
                batchBuilds.Add(MakeBuild(++maxSeq, targetNodeId, channelId, currentEvents, clock.UtcNow));
        }

        if (batchBuilds.Count == 0) return [];

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.OutgoingBatches.AddRange(batchBuilds.Select(b => b.Batch));
        await db.SaveChangesAsync(ct); // generates BatchIds

        var links = batchBuilds.SelectMany(b =>
            b.Events.Select(e => new SyncDataEventBatch { EventId = e.EventId, BatchId = b.Batch.BatchId }));
        db.DataEventBatches.AddRange(links);

        var processedIds = batchBuilds.SelectMany(b => b.Events.Select(e => e.EventId)).Distinct().ToList();
        await db.DataEvents.Where(e => processedIds.Contains(e.EventId))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsProcessed, true), ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return batchBuilds.Select(b => b.Batch).ToList().AsReadOnly();
    }

    private static (SyncOutgoingBatch Batch, List<SyncDataEvent> Events) MakeBuild(
        long seq, string nodeId, string channelId, List<SyncDataEvent> events, DateTime now)
    {
        var batch = new SyncOutgoingBatch
        {
            BatchSequence = seq,
            NodeId        = nodeId,
            ChannelId     = channelId,
            Status        = (byte)BatchStatus.New,
            RowCount      = events.Count,
            ByteCount     = events.Sum(e => (long)(e.RowData?.Length ?? 0) * 2),
            CreateTime    = now
        };
        return (batch, new List<SyncDataEvent>(events));
    }
}
```

- [ ] **Step 7: Create `GzipBatchCompressor`**

```csharp
// src/MSOSync.Batch/GzipBatchCompressor.cs
using System.IO.Compression;

namespace MSOSync.Batch;

public sealed class GzipBatchCompressor
{
    public byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal))
            gz.Write(data);
        return output.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var input  = new MemoryStream(data);
        using var output = new MemoryStream();
        using (var gz = new GZipStream(input, CompressionMode.Decompress))
            gz.CopyTo(output);
        return output.ToArray();
    }
}
```

- [ ] **Step 8: Create `RetryProcessor`**

```csharp
// src/MSOSync.Batch/RetryProcessor.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class RetryProcessor(
    AppDbContext db,
    IBatchStateMachine stateMachine,
    IClock clock,
    ILogger<RetryProcessor> logger)
{
    private const int MaxRetries = 5;

    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var candidates = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Error
                     && b.RetryCount < MaxRetries
                     && (b.NextRetryTime == null || b.NextRetryTime <= now))
            .ToListAsync(ct);

        var count = 0;
        foreach (var batch in candidates)
        {
            var succeeded = await stateMachine.TransitionAsync(
                batch.BatchId, BatchStatus.Error, BatchStatus.Retry, ct);

            if (!succeeded) continue;

            var delay = TimeSpan.FromMinutes(Math.Pow(2, batch.RetryCount)); // 2^n × 5 min base
            delay = TimeSpan.FromMinutes(delay.TotalMinutes * 5);             // scale by 5

            batch.RetryCount++;
            batch.NextRetryTime = now + delay;
            await db.SaveChangesAsync(ct);
            count++;

            logger.LogInformation("Batch {BatchId} queued for retry #{Count}, next={Next:u}",
                batch.BatchId, batch.RetryCount, batch.NextRetryTime);
        }

        return count;
    }
}
```

> **Delay formula:** `delay = 2^(retryCount-1) × 5 min`. When `RetryCount=0` (first error), delay = `2^0 × 5 = 5 min`. When `RetryCount=1`, delay = `2^1 × 5 = 10 min`. The code above increments `RetryCount` after transition, so use `batch.RetryCount` (pre-increment) as the exponent: `Math.Pow(2, batch.RetryCount) * 5` minutes.

Correct formula in code (fix the two-line approach above to a single correct line):

```csharp
var delayMinutes = Math.Pow(2, batch.RetryCount) * 5.0; // RetryCount is pre-increment
batch.NextRetryTime = now.AddMinutes(delayMinutes);
batch.RetryCount++;
```

Replace Steps 8's `delay` computation with this exact code.

- [ ] **Step 9: Create `BatchPurger`**

```csharp
// src/MSOSync.Batch/BatchPurger.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class BatchPurger(AppDbContext db, IClock clock, ILogger<BatchPurger> logger)
{
    private const int DefaultRetentionDays = 30;
    private const string RetentionParam    = "batch.retention.days";

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var param = await db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == RetentionParam, ct);
        var days   = int.TryParse(param?.ParameterValue, out var d) ? d : DefaultRetentionDays;
        var cutoff = clock.UtcNow.AddDays(-days);

        var deleted = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Ok && b.CreateTime < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("BatchPurger deleted {Count} Ok batches older than {Cutoff:u}", deleted, cutoff);
        return deleted;
    }
}
```

- [ ] **Step 10: Create `BatchPipelineExtensions`**

```csharp
// src/MSOSync.Batch/BatchPipelineExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Batch;

public static class BatchPipelineExtensions
{
    public static IServiceCollection AddBatchPipeline(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddScoped<IBatchStateMachine, BatchStateMachine>();
        services.AddScoped<IBatchCreator, BatchCreator>();
        services.AddSingleton<GzipBatchCompressor>();
        services.AddScoped<RetryProcessor>();
        services.AddScoped<BatchPurger>();
        return services;
    }
}
```

- [ ] **Step 11: Delete `Placeholder.cs`**

```pwsh
Remove-Item src/MSOSync.Batch/Placeholder.cs
```

- [ ] **Step 12: Build**

```pwsh
dotnet build src/MSOSync.Batch/MSOSync.Batch.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 13: Commit**

```pwsh
git add src/MSOSync.Batch/BatchStatus.cs `
        src/MSOSync.Batch/OutgoingBatchDto.cs `
        src/MSOSync.Batch/IBatchStateMachine.cs `
        src/MSOSync.Batch/BatchStateMachine.cs `
        src/MSOSync.Batch/IBatchCreator.cs `
        src/MSOSync.Batch/BatchCreator.cs `
        src/MSOSync.Batch/GzipBatchCompressor.cs `
        src/MSOSync.Batch/RetryProcessor.cs `
        src/MSOSync.Batch/BatchPurger.cs `
        src/MSOSync.Batch/BatchPipelineExtensions.cs
git rm src/MSOSync.Batch/Placeholder.cs
git commit -m "feat(batch): add BatchStateMachine, BatchCreator, RetryProcessor, BatchPurger, GzipBatchCompressor"
```
