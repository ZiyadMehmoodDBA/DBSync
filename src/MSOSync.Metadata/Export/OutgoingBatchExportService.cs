using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Export;

public sealed class OutgoingBatchExportService(AppDbContext db) : IExportService<OutgoingBatchExportFilter>
{
    private const string CsvHeader = "batchId,status,nodeId,channelId,createTime,sentTime,ackTime,retryCount,rowCount";

    // Mirrors MSOSync.Batch.BatchStatus without creating a circular project reference.
    private static readonly Dictionary<string, byte> StatusMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "New",          0 },
            { "Sending",      1 },
            { "Acknowledged", 2 },
            { "Error",        3 },
            { "Retry",        4 },
        };

    // Reverse lookup: byte value → canonical string name for serialization.
    private static readonly Dictionary<byte, string> StatusNameMap =
        StatusMap.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>Returns true if <paramref name="status"/> is a known status name.</summary>
    public static bool IsValidStatus(string status) => StatusMap.ContainsKey(status);

    public async Task<int> ExportCsvAsync(Stream output, OutgoingBatchExportFilter filter, CancellationToken ct)
    {
        using var writer = new StreamWriter(output, leaveOpen: true);
        await writer.WriteLineAsync(CsvHeader);
        int count = 0;
        await foreach (var r in BuildQuery(filter).AsAsyncEnumerable().WithCancellation(ct))
        {
            var statusName = StatusNameMap.TryGetValue(r.Status, out var n) ? n : r.Status.ToString();
            await writer.WriteLineAsync(
                $"{r.BatchId},{statusName},{CsvHelper.Escape(r.NodeId)},{CsvHelper.Escape(r.ChannelId)},{r.CreateTime?.ToString("O") ?? ""},{r.SentTime?.ToString("O") ?? ""},{r.AckTime?.ToString("O") ?? ""},{r.RetryCount},{r.RowCount}");
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
            writer.WriteNumber("batchId",    r.BatchId);
            writer.WriteString("status",     StatusNameMap.TryGetValue(r.Status, out var sn) ? sn : r.Status.ToString());
            writer.WriteString("nodeId",     r.NodeId);
            writer.WriteString("channelId",  r.ChannelId);
            if (r.CreateTime.HasValue) writer.WriteString("createTime", r.CreateTime.Value.ToString("O"));
            else                       writer.WriteNull("createTime");
            if (r.SentTime.HasValue)   writer.WriteString("sentTime",   r.SentTime.Value.ToString("O"));
            else                       writer.WriteNull("sentTime");
            if (r.AckTime.HasValue)    writer.WriteString("ackTime",    r.AckTime.Value.ToString("O"));
            else                       writer.WriteNull("ackTime");
            writer.WriteNumber("retryCount", r.RetryCount);
            writer.WriteNumber("rowCount",   r.RowCount);
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
            StatusMap.TryGetValue(filter.Status, out var statusByte))
            q = q.Where(b => b.Status == statusByte);
        if (filter.From.HasValue)
            q = q.Where(b => b.CreateTime.HasValue && b.CreateTime.Value >= filter.From.Value);
        if (filter.To.HasValue)
            q = q.Where(b => b.CreateTime.HasValue && b.CreateTime.Value <= filter.To.Value);
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
        DateTime? CreateTime,
        DateTime? SentTime,
        DateTime? AckTime,
        int       RetryCount,
        int       RowCount);
}
