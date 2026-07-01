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
