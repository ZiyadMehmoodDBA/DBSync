# Task 1: Backend Export Streaming

**Part of:** Epic 11D — Export + Audit Intelligence  
**Spec:** `docs/superpowers/specs/2026-07-01-epic11d-export-audit-intelligence-design.md`

## Files

**Create:**
- `src/MSOSync.Metadata/Export/IExportService.cs`
- `src/MSOSync.Metadata/Export/CsvHelper.cs`
- `src/MSOSync.Metadata/Export/IExportAuditService.cs`
- `src/MSOSync.Metadata/Export/ExportAuditService.cs`
- `src/MSOSync.Metadata/Export/EventExportService.cs`
- `src/MSOSync.Metadata/Export/IncomingBatchExportService.cs`
- `src/MSOSync.Metadata/Export/OutgoingBatchExportService.cs`
- `src/MSOSync.Metadata/Export/AuditExportService.cs`
- `src/MSOSync.Api/Results/StreamingExportResult.cs`
- `tests/MSOSync.MetadataTests/Export/EventExportServiceTests.cs`
- `tests/MSOSync.MetadataTests/Export/AuditExportServiceTests.cs`

**Modify:**
- `src/MSOSync.Api/Controllers/EventsController.cs`
- `src/MSOSync.Api/Controllers/IncomingBatchesController.cs`
- `src/MSOSync.Api/Controllers/BatchController.cs`
- `src/MSOSync.Api/Controllers/AuditController.cs`
- `src/MSOSync.Metadata/MetadataServiceExtensions.cs`

## Interfaces Produced (consumed by nothing in this task; controllers call them)

```csharp
// IExportService<TFilter>
Task<int> ExportCsvAsync(Stream output, TFilter filter, CancellationToken ct);
Task<int> ExportJsonAsync(Stream output, TFilter filter, CancellationToken ct);

// IExportAuditService
Task WriteAsync(string resource, string format, int rowCount, long durationMs);
```

---

## Global Constraints (apply to every step)

- C# 13, .NET 9, `TreatWarningsAsErrors = true`
- EF Core 9: use `AsAsyncEnumerable()` for streaming — never buffer full result with `ToListAsync()`
- No new NuGet packages
- Auth policy `"ViewerOrAbove"` on all export endpoints
- Export audit: `ActionName = "EXPORT_{RESOURCE}"`, `ObjectName = "{resource}|{format}|{rowCount}|{durationMs}"` (varchar(100), e.g. `"events|csv|4821|312"`)
- Unit tests use `TestDbContext.Create()` (SQLite in-memory)

---

- [ ] **Step 1: Create IExportService, CsvHelper, IExportAuditService**

```csharp
// src/MSOSync.Metadata/Export/IExportService.cs
namespace MSOSync.Metadata.Export;

public interface IExportService<TFilter>
{
    Task<int> ExportCsvAsync(Stream output, TFilter filter, CancellationToken ct);
    Task<int> ExportJsonAsync(Stream output, TFilter filter, CancellationToken ct);
}
```

```csharp
// src/MSOSync.Metadata/Export/CsvHelper.cs
namespace MSOSync.Metadata.Export;

internal static class CsvHelper
{
    internal static string Escape(string? s)
    {
        if (s is null) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }
}
```

```csharp
// src/MSOSync.Metadata/Export/IExportAuditService.cs
namespace MSOSync.Metadata.Export;

public interface IExportAuditService
{
    Task WriteAsync(string resource, string format, int rowCount, long durationMs);
}
```

- [ ] **Step 2: Create ExportAuditService**

```csharp
// src/MSOSync.Metadata/Export/ExportAuditService.cs
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Export;

public sealed class ExportAuditService(AppDbContext db, ICurrentUserService currentUser) : IExportAuditService
{
    public async Task WriteAsync(string resource, string format, int rowCount, long durationMs)
    {
        db.Audits.Add(new SyncAudit
        {
            ActionName = $"EXPORT_{resource.ToUpperInvariant().Replace('-', '_')}",
            ObjectName = $"{resource}|{format}|{rowCount}|{durationMs}",
            Username   = currentUser.GetCurrentUsername(),
            CreateTime = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 3: Create StreamingExportResult**

```csharp
// src/MSOSync.Api/Results/StreamingExportResult.cs
using Microsoft.AspNetCore.Mvc;

namespace MSOSync.Api.Results;

public sealed class StreamingExportResult : IActionResult
{
    private readonly Func<Stream, CancellationToken, Task<int>> _writer;
    private readonly string _contentType;
    private readonly string _fileName;
    private readonly Func<int, long, Task>? _onComplete;

    public StreamingExportResult(
        Func<Stream, CancellationToken, Task<int>> writer,
        string contentType,
        string fileName,
        Func<int, long, Task>? onComplete = null)
    {
        _writer     = writer;
        _contentType = contentType;
        _fileName   = fileName;
        _onComplete = onComplete;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = _contentType;
        response.Headers["Content-Disposition"] = $"attachment; filename=\"{_fileName}\"";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rowCount = await _writer(response.Body, context.HttpContext.RequestAborted);
        sw.Stop();
        if (_onComplete is not null)
        {
            try { await _onComplete(rowCount, sw.ElapsedMilliseconds); }
            catch { /* best effort — do not fail the response */ }
        }
    }
}
```

- [ ] **Step 4: Write failing test for EventExportService**

First, check `src/MSOSync.Persistence/AppDbContext.cs` to find the exact entity type for `db.DataEvents` (it's likely `SyncDataEvent`). Use that type in the test factory below.

```csharp
// tests/MSOSync.MetadataTests/Export/EventExportServiceTests.cs
using FluentAssertions;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Export;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Export;

public sealed class EventExportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EventExportService _sut;

    public EventExportServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new EventExportService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncDataEvent Event(long id,
        string sourceNodeId = "N1", string tableName = "dbo.Items",
        DateTime? createTime = null) => new()
    {
        EventId      = id,
        TriggerId    = "T1",
        SourceNodeId = sourceNodeId,
        ChannelId    = "C1",
        EventType    = 'I',
        TableName    = tableName,
        CreateTime   = createTime ?? DateTime.UtcNow,
        IsProcessed  = false,
    };

    [Fact]
    public async Task ExportCsv_WritesHeaderAndDataRows()
    {
        _db.DataEvents.AddRange(Event(1), Event(2));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new EventFilter(), default);

        count.Should().Be(2);
        ms.Position = 0;
        var lines = new StreamReader(ms).ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("eventId,");
        lines.Should().HaveCount(3); // header + 2 rows
    }

    [Fact]
    public async Task ExportCsv_FilterApplied_ReturnsMatchingCount()
    {
        _db.DataEvents.AddRange(Event(1, sourceNodeId: "N1"), Event(2, sourceNodeId: "N2"));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new EventFilter { SourceNodeId = "N1" }, default);

        count.Should().Be(1);
    }

    [Fact]
    public async Task ExportCsv_FieldContainsComma_IsQuoted()
    {
        _db.DataEvents.Add(Event(1, tableName: "dbo.Cust,omers"));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        await _sut.ExportCsvAsync(ms, new EventFilter(), default);

        ms.Position = 0;
        new StreamReader(ms).ReadToEnd().Should().Contain("\"dbo.Cust,omers\"");
    }

    [Fact]
    public async Task ExportJson_ProducesValidJsonArray()
    {
        _db.DataEvents.Add(Event(1));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportJsonAsync(ms, new EventFilter(), default);

        count.Should().Be(1);
        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        json.Should().StartWith("[").And.EndWith("]");
    }

    [Fact]
    public async Task ExportCsv_EmptyResult_WritesHeaderOnly()
    {
        using var ms = new MemoryStream();
        var count = await _sut.ExportCsvAsync(ms, new EventFilter(), default);
        count.Should().Be(0);
        ms.Position = 0;
        var lines = new StreamReader(ms).ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1); // header only
    }
}
```

- [ ] **Step 5: Run test — verify it fails with "EventExportService not found"**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~EventExportServiceTests" 2>&1 | Select-Object -Last 10
```

Expected: build error (type not found).

- [ ] **Step 6: Create EventExportService**

```csharp
// src/MSOSync.Metadata/Export/EventExportService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Export;

public sealed class EventExportService(AppDbContext db) : IExportService<EventFilter>
{
    private const string CsvHeader = "eventId,triggerId,sourceNodeId,channelId,eventType,tableName,createTime,isProcessed";

    public async Task<int> ExportCsvAsync(Stream output, EventFilter filter, CancellationToken ct)
    {
        using var writer = new StreamWriter(output, leaveOpen: true);
        await writer.WriteLineAsync(CsvHeader);
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteLineAsync(
                $"{r.EventId},{CsvHelper.Escape(r.TriggerId)},{CsvHelper.Escape(r.SourceNodeId)},{CsvHelper.Escape(r.ChannelId)},{r.EventType},{CsvHelper.Escape(r.TableName)},{r.CreateTime:O},{r.IsProcessed}");
            count++;
        }
        await writer.FlushAsync(ct);
        return count;
    }

    public async Task<int> ExportJsonAsync(Stream output, EventFilter filter, CancellationToken ct)
    {
        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartArray();
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteNumber("eventId",      r.EventId);
            writer.WriteString("triggerId",    r.TriggerId);
            writer.WriteString("sourceNodeId", r.SourceNodeId);
            writer.WriteString("channelId",    r.ChannelId);
            writer.WriteString("eventType",    r.EventType.ToString());
            writer.WriteString("tableName",    r.TableName);
            writer.WriteString("createTime",   r.CreateTime.ToString("O"));
            writer.WriteBoolean("isProcessed", r.IsProcessed);
            writer.WriteEndObject();
            await writer.FlushAsync(ct);
            count++;
        }
        writer.WriteEndArray();
        await writer.FlushAsync(ct);
        return count;
    }

    private IQueryable<EventExportRow> BuildQuery(EventFilter filter)
    {
        var q = db.DataEvents.AsNoTracking();
        if (filter.SourceNodeId is not null) q = q.Where(e => e.SourceNodeId == filter.SourceNodeId);
        if (filter.TriggerId    is not null) q = q.Where(e => e.TriggerId    == filter.TriggerId);
        if (filter.ChannelId    is not null) q = q.Where(e => e.ChannelId    == filter.ChannelId);
        if (filter.EventType    is not null) q = q.Where(e => e.EventType    == filter.EventType);
        if (filter.IsProcessed  is not null) q = q.Where(e => e.IsProcessed  == filter.IsProcessed);
        if (filter.From         is not null) q = q.Where(e => e.CreateTime   >= filter.From);
        if (filter.To           is not null) q = q.Where(e => e.CreateTime   <= filter.To);
        return q.OrderByDescending(e => e.CreateTime)
            .Select(e => new EventExportRow(
                e.EventId, e.TriggerId, e.SourceNodeId, e.ChannelId,
                e.EventType, e.TableName, e.CreateTime, e.IsProcessed));
    }

    private sealed record EventExportRow(
        long     EventId,
        string   TriggerId,
        string   SourceNodeId,
        string   ChannelId,
        char     EventType,
        string   TableName,
        DateTime CreateTime,
        bool     IsProcessed);
}
```

- [ ] **Step 7: Run EventExportService tests — all must pass**

```pwsh
dotnet test tests/MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~EventExportServiceTests" 2>&1 | Select-Object -Last 10
```

Expected: 5 tests pass.

- [ ] **Step 8: Write failing test for AuditExportService**

```csharp
// tests/MSOSync.MetadataTests/Export/AuditExportServiceTests.cs
using FluentAssertions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Export;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Export;

public sealed class AuditExportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuditExportService _sut;

    public AuditExportServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new AuditExportService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncAudit Audit(long id, string? username = "alice",
        string? actionName = "LOGIN_SUCCESS", DateTime? createTime = null) => new()
    {
        AuditId    = id,
        Username   = username,
        ActionName = actionName,
        ObjectName = null,
        CreateTime = createTime ?? DateTime.UtcNow,
    };

    [Fact]
    public async Task ExportCsv_WritesHeaderAndDataRows()
    {
        _db.Audits.AddRange(Audit(1), Audit(2));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new AuditFilter(), default);

        count.Should().Be(2);
        ms.Position = 0;
        var lines = new StreamReader(ms).ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("auditId,");
        lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExportCsv_FilterByUsername_ReturnsMatchingCount()
    {
        _db.Audits.AddRange(Audit(1, username: "alice"), Audit(2, username: "bob"));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new AuditFilter { Username = "alice" }, default);

        count.Should().Be(1);
    }

    [Fact]
    public async Task ExportJson_ProducesValidJsonArray()
    {
        _db.Audits.Add(Audit(1));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportJsonAsync(ms, new AuditFilter(), default);

        count.Should().Be(1);
        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        json.Should().StartWith("[").And.EndWith("]");
    }

    [Fact]
    public async Task ExportCsv_NullCreateTime_ExcludesRow()
    {
        _db.Audits.AddRange(
            new SyncAudit { AuditId = 1, Username = "alice", CreateTime = DateTime.UtcNow },
            new SyncAudit { AuditId = 2, Username = "bob",   CreateTime = null });
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new AuditFilter(), default);

        count.Should().Be(1);
    }
}
```

- [ ] **Step 9: Run AuditExportService test — verify it fails**

```pwsh
dotnet test tests/MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~AuditExportServiceTests" 2>&1 | Select-Object -Last 5
```

Expected: build error (type not found).

- [ ] **Step 10: Create IncomingBatchExportService, OutgoingBatchExportService, AuditExportService**

Check `src/MSOSync.Persistence/AppDbContext.cs` for entity types of `db.IncomingBatches` and `db.OutgoingBatches`. Use those exact types in the Select projections.

```csharp
// src/MSOSync.Metadata/Export/IncomingBatchExportService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Export;

public sealed class IncomingBatchExportService(AppDbContext db) : IExportService<IncomingBatchFilter>
{
    private const string CsvHeader = "batchId,sourceNodeId,channelId,status,rowCount,batchSequence,receivedTime,applyTimeMs";

    public async Task<int> ExportCsvAsync(Stream output, IncomingBatchFilter filter, CancellationToken ct)
    {
        using var writer = new StreamWriter(output, leaveOpen: true);
        await writer.WriteLineAsync(CsvHeader);
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteLineAsync(
                $"{r.BatchId},{CsvHelper.Escape(r.SourceNodeId)},{CsvHelper.Escape(r.ChannelId)},{r.Status},{r.RowCount?.ToString() ?? ""},{r.BatchSequence},{r.ReceivedTime:O},{r.ApplyTimeMs?.ToString() ?? ""}");
            count++;
        }
        await writer.FlushAsync(ct);
        return count;
    }

    public async Task<int> ExportJsonAsync(Stream output, IncomingBatchFilter filter, CancellationToken ct)
    {
        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartArray();
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteNumber("batchId",       r.BatchId);
            writer.WriteString("sourceNodeId",  r.SourceNodeId);
            writer.WriteString("channelId",     r.ChannelId);
            writer.WriteString("status",        r.Status.ToString());
            if (r.RowCount.HasValue) writer.WriteNumber("rowCount", r.RowCount.Value);
            else writer.WriteNull("rowCount");
            writer.WriteNumber("batchSequence", r.BatchSequence);
            writer.WriteString("receivedTime",  r.ReceivedTime.ToString("O"));
            if (r.ApplyTimeMs.HasValue) writer.WriteNumber("applyTimeMs", r.ApplyTimeMs.Value);
            else writer.WriteNull("applyTimeMs");
            writer.WriteEndObject();
            await writer.FlushAsync(ct);
            count++;
        }
        writer.WriteEndArray();
        await writer.FlushAsync(ct);
        return count;
    }

    private IQueryable<IncomingBatchExportRow> BuildQuery(IncomingBatchFilter filter)
    {
        var q = db.IncomingBatches.AsNoTracking();
        if (filter.SourceNodeId is not null) q = q.Where(b => b.SourceNodeId == filter.SourceNodeId);
        if (filter.ChannelId    is not null) q = q.Where(b => b.ChannelId    == filter.ChannelId);
        if (filter.Status       is not null) q = q.Where(b => b.Status       == filter.Status);
        if (filter.From         is not null) q = q.Where(b => b.ReceivedTime >= filter.From);
        if (filter.To           is not null) q = q.Where(b => b.ReceivedTime <= filter.To);
        return q.OrderByDescending(b => b.ReceivedTime)
            .Select(b => new IncomingBatchExportRow(
                b.BatchId, b.SourceNodeId, b.ChannelId, b.Status,
                b.RowCount, b.BatchSequence, b.ReceivedTime, b.ApplyTimeMs));
    }

    private sealed record IncomingBatchExportRow(
        long                BatchId,
        string              SourceNodeId,
        string              ChannelId,
        MSOSync.Persistence.IncomingBatchStatus Status,
        int?                RowCount,
        long                BatchSequence,
        DateTime            ReceivedTime,
        long?               ApplyTimeMs);
}
```

```csharp
// src/MSOSync.Metadata/Export/OutgoingBatchExportFilter.cs
namespace MSOSync.Metadata.Export;

public sealed class OutgoingBatchExportFilter
{
    public string? NodeId    { get; set; }
    public string? ChannelId { get; set; }
    public string? Status    { get; set; }  // parsed to BatchStatus enum in service
}
```

```csharp
// src/MSOSync.Metadata/Export/OutgoingBatchExportService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Export;

public sealed class OutgoingBatchExportService(AppDbContext db) : IExportService<OutgoingBatchExportFilter>
{
    private const string CsvHeader = "batchId,status,nodeId,channelId,createTime,sentTime,ackTime,retryCount,rowCount";

    public async Task<int> ExportCsvAsync(Stream output, OutgoingBatchExportFilter filter, CancellationToken ct)
    {
        using var writer = new StreamWriter(output, leaveOpen: true);
        await writer.WriteLineAsync(CsvHeader);
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteLineAsync(
                $"{r.BatchId},{r.Status},{CsvHelper.Escape(r.NodeId)},{CsvHelper.Escape(r.ChannelId)},{r.CreateTime:O},{r.SentTime?.ToString("O") ?? ""},{r.AckTime?.ToString("O") ?? ""},{r.RetryCount},{r.RowCount?.ToString() ?? ""}");
            count++;
        }
        await writer.FlushAsync(ct);
        return count;
    }

    public async Task<int> ExportJsonAsync(Stream output, OutgoingBatchExportFilter filter, CancellationToken ct)
    {
        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartArray();
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteNumber("batchId",     r.BatchId);
            writer.WriteNumber("status",      r.Status);
            writer.WriteString("nodeId",      r.NodeId);
            writer.WriteString("channelId",   r.ChannelId);
            writer.WriteString("createTime",  r.CreateTime.ToString("O"));
            if (r.SentTime.HasValue)   writer.WriteString("sentTime",   r.SentTime.Value.ToString("O"));
            else                       writer.WriteNull("sentTime");
            if (r.AckTime.HasValue)    writer.WriteString("ackTime",    r.AckTime.Value.ToString("O"));
            else                       writer.WriteNull("ackTime");
            writer.WriteNumber("retryCount",  r.RetryCount);
            if (r.RowCount.HasValue)   writer.WriteNumber("rowCount",   r.RowCount.Value);
            else                       writer.WriteNull("rowCount");
            writer.WriteEndObject();
            await writer.FlushAsync(ct);
            count++;
        }
        writer.WriteEndArray();
        await writer.FlushAsync(ct);
        return count;
    }

    private IQueryable<OutgoingBatchExportRow> BuildQuery(OutgoingBatchExportFilter filter)
    {
        var q = db.OutgoingBatches.AsNoTracking();
        if (filter.NodeId    is not null) q = q.Where(b => b.NodeId    == filter.NodeId);
        if (filter.ChannelId is not null) q = q.Where(b => b.ChannelId == filter.ChannelId);
        if (!string.IsNullOrEmpty(filter.Status) &&
            Enum.TryParse<MSOSync.Batch.BatchStatus>(filter.Status, ignoreCase: true, out var status))
            q = q.Where(b => b.Status == (byte)status);
        return q.OrderByDescending(b => b.BatchId)
            .Select(b => new OutgoingBatchExportRow(
                b.BatchId, b.Status, b.NodeId, b.ChannelId,
                b.CreateTime, b.SentTime, b.AckTime, b.RetryCount, b.RowCount));
    }

    private sealed record OutgoingBatchExportRow(
        long      BatchId,
        byte      Status,
        string    NodeId,
        string    ChannelId,
        DateTime  CreateTime,
        DateTime? SentTime,
        DateTime? AckTime,
        int       RetryCount,
        int?      RowCount);
}
```

```csharp
// src/MSOSync.Metadata/Export/AuditExportService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Export;

public sealed class AuditExportService(AppDbContext db) : IExportService<AuditFilter>
{
    private const string CsvHeader = "auditId,username,actionName,objectName,correlationId,createTime";

    public async Task<int> ExportCsvAsync(Stream output, AuditFilter filter, CancellationToken ct)
    {
        using var writer = new StreamWriter(output, leaveOpen: true);
        await writer.WriteLineAsync(CsvHeader);
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            await writer.WriteLineAsync(
                $"{r.AuditId},{CsvHelper.Escape(r.Username)},{CsvHelper.Escape(r.ActionName)},{CsvHelper.Escape(r.ObjectName)},{CsvHelper.Escape(r.CorrelationId)},{r.CreateTime:O}");
            count++;
        }
        await writer.FlushAsync(ct);
        return count;
    }

    public async Task<int> ExportJsonAsync(Stream output, AuditFilter filter, CancellationToken ct)
    {
        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartArray();
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            writer.WriteNumber("auditId",       r.AuditId);
            writer.WriteString("username",      r.Username);
            writer.WriteString("actionName",    r.ActionName);
            writer.WriteString("objectName",    r.ObjectName);
            writer.WriteString("correlationId", r.CorrelationId);
            writer.WriteString("createTime",    r.CreateTime.ToString("O"));
            writer.WriteEndObject();
            await writer.FlushAsync(ct);
            count++;
        }
        writer.WriteEndArray();
        await writer.FlushAsync(ct);
        return count;
    }

    private IQueryable<AuditExportRow> BuildQuery(AuditFilter filter)
    {
        var q = db.Audits.AsNoTracking().Where(a => a.CreateTime != null);
        if (filter.Username   is not null) q = q.Where(a => a.Username   == filter.Username);
        if (filter.ActionName is not null) q = q.Where(a => a.ActionName == filter.ActionName);
        if (filter.From       is not null) q = q.Where(a => a.CreateTime >= filter.From);
        if (filter.To         is not null) q = q.Where(a => a.CreateTime <= filter.To);
        return q.OrderByDescending(a => a.CreateTime)
            .Select(a => new AuditExportRow(
                a.AuditId, a.Username, a.ActionName,
                a.ObjectName, a.CorrelationId, a.CreateTime!.Value));
    }

    private sealed record AuditExportRow(
        long     AuditId,
        string?  Username,
        string?  ActionName,
        string?  ObjectName,
        string?  CorrelationId,
        DateTime CreateTime);
}
```

- [ ] **Step 11: Run AuditExportService tests — all must pass**

```pwsh
dotnet test tests/MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~AuditExportServiceTests" 2>&1 | Select-Object -Last 10
```

Expected: 4 tests pass.

- [ ] **Step 12: Add export actions to the four controllers**

In `EventsController.cs`, add `IExportService<EventFilter> exporter, IExportAuditService exportAudit` to the primary constructor, then add:

```csharp
// Add to EventsController — inside the class, after GetEventById
[HttpGet("export")]
[ProducesResponseType(200)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
public async Task<IActionResult> ExportEvents(
    [FromQuery] EventFilter filter,
    [FromQuery] string format = "csv",
    CancellationToken ct = default)
{
    await validator.ValidateAndThrowAsync(filter, ct);
    var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
    var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
    return new MSOSync.Api.Results.StreamingExportResult(
        isJson
            ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
            : (s, t) => exporter.ExportCsvAsync(s, filter, t),
        isJson ? "application/json" : "text/csv",
        isJson ? $"events-{date}.json" : $"events-{date}.csv",
        (rows, ms) => exportAudit.WriteAsync("events", format, rows, ms));
}
```

In `IncomingBatchesController.cs`, add `IExportService<IncomingBatchFilter> exporter, IExportAuditService exportAudit` to the constructor, then add:

```csharp
[HttpGet("export")]
[ProducesResponseType(200)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
public async Task<IActionResult> ExportIncomingBatches(
    [FromQuery] IncomingBatchFilter filter,
    [FromQuery] string format = "csv",
    CancellationToken ct = default)
{
    await validator.ValidateAndThrowAsync(filter, ct);
    var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
    var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
    return new MSOSync.Api.Results.StreamingExportResult(
        isJson
            ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
            : (s, t) => exporter.ExportCsvAsync(s, filter, t),
        isJson ? "application/json" : "text/csv",
        isJson ? $"incoming-batches-{date}.json" : $"incoming-batches-{date}.csv",
        (rows, ms) => exportAudit.WriteAsync("incoming-batches", format, rows, ms));
}
```

In `BatchController.cs`, add `MSOSync.Metadata.Export.IExportService<MSOSync.Metadata.Export.OutgoingBatchExportFilter> exporter, MSOSync.Metadata.Export.IExportAuditService exportAudit` to the constructor, then add:

```csharp
[HttpGet("export")]
[Authorize]
public IActionResult ExportBatches(
    [FromQuery] MSOSync.Metadata.Export.OutgoingBatchExportFilter filter,
    [FromQuery] string format = "csv")
{
    var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
    var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
    return new MSOSync.Api.Results.StreamingExportResult(
        isJson
            ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
            : (s, t) => exporter.ExportCsvAsync(s, filter, t),
        isJson ? "application/json" : "text/csv",
        isJson ? $"batches-{date}.json" : $"batches-{date}.csv",
        (rows, ms) => exportAudit.WriteAsync("outgoing-batches", format, rows, ms));
}
```

In `AuditController.cs`, add `IExportService<AuditFilter> exporter, IExportAuditService exportAudit` to the constructor, then add:

```csharp
[HttpGet("export")]
[ProducesResponseType(200)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
public async Task<IActionResult> ExportAudit(
    [FromQuery] AuditFilter filter,
    [FromQuery] string format = "csv",
    CancellationToken ct = default)
{
    await validator.ValidateAndThrowAsync(filter, ct);
    var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
    var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
    return new MSOSync.Api.Results.StreamingExportResult(
        isJson
            ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
            : (s, t) => exporter.ExportCsvAsync(s, filter, t),
        isJson ? "application/json" : "text/csv",
        isJson ? $"audit-{date}.json" : $"audit-{date}.csv",
        (rows, ms) => exportAudit.WriteAsync("audit", format, rows, ms));
}
```

- [ ] **Step 13: Register services in MetadataServiceExtensions.cs**

Add these lines inside the `AddMetadata` method, after the Epic 9D block:

```csharp
// Epic 11D — Export streaming
services.AddScoped<IExportService<MSOSync.Metadata.Events.EventFilter>,             MSOSync.Metadata.Export.EventExportService>();
services.AddScoped<IExportService<MSOSync.Metadata.IncomingBatches.IncomingBatchFilter>, MSOSync.Metadata.Export.IncomingBatchExportService>();
services.AddScoped<IExportService<MSOSync.Metadata.Export.OutgoingBatchExportFilter>, MSOSync.Metadata.Export.OutgoingBatchExportService>();
services.AddScoped<IExportService<MSOSync.Metadata.Audit.AuditFilter>,               MSOSync.Metadata.Export.AuditExportService>();
services.AddScoped<IExportAuditService, ExportAuditService>();
```

Add the namespace at the top of MetadataServiceExtensions.cs:
```csharp
using MSOSync.Metadata.Export;
```

- [ ] **Step 14: Build the solution — zero warnings, zero errors**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror 2>&1 | Select-Object -Last 20
```

Expected: `Build succeeded. 0 Error(s) 0 Warning(s)`

- [ ] **Step 15: Run all MetadataTests — confirm all pass**

```pwsh
dotnet test tests/MSOSync.MetadataTests -c Debug 2>&1 | Select-Object -Last 10
```

Expected: all tests pass.

- [ ] **Step 16: Commit**

```pwsh
git add `
  src/MSOSync.Metadata/Export/IExportService.cs `
  src/MSOSync.Metadata/Export/CsvHelper.cs `
  src/MSOSync.Metadata/Export/IExportAuditService.cs `
  src/MSOSync.Metadata/Export/ExportAuditService.cs `
  src/MSOSync.Metadata/Export/EventExportService.cs `
  src/MSOSync.Metadata/Export/IncomingBatchExportService.cs `
  src/MSOSync.Metadata/Export/OutgoingBatchExportFilter.cs `
  src/MSOSync.Metadata/Export/OutgoingBatchExportService.cs `
  src/MSOSync.Metadata/Export/AuditExportService.cs `
  src/MSOSync.Api/Results/StreamingExportResult.cs `
  src/MSOSync.Api/Controllers/EventsController.cs `
  src/MSOSync.Api/Controllers/IncomingBatchesController.cs `
  src/MSOSync.Api/Controllers/BatchController.cs `
  src/MSOSync.Api/Controllers/AuditController.cs `
  src/MSOSync.Metadata/MetadataServiceExtensions.cs `
  tests/MSOSync.MetadataTests/Export/EventExportServiceTests.cs `
  tests/MSOSync.MetadataTests/Export/AuditExportServiceTests.cs

git commit -m "feat: add streaming CSV/JSON export to events, batches, and audit endpoints

IExportService<TFilter> with IAsyncEnumerable streaming, StreamingExportResult writes
directly to Response.Body, ExportAuditService logs resource/format/rowCount/durationMs."
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Tests: <N> passed, 0 failed
Concerns: <none or list>
```
