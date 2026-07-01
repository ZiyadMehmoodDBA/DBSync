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
