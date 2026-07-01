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
