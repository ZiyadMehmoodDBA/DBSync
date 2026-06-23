// src/MSOSync.Engine/Metadata/TriggerApplyMetadataService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Engine;

public sealed class TriggerApplyMetadataService(AppDbContext db) : ITriggerApplyMetadataService
{
    public async Task<Dictionary<string, TriggerApplyMetadata>> GetMetadataAsync(
        IReadOnlyList<string> triggerIds,
        CancellationToken     ct = default)
    {
        var triggers = await db.Triggers
            .Where(t => triggerIds.Contains(t.TriggerId))
            .ToListAsync(ct);

        return triggers.ToDictionary(
            t => t.TriggerId,
            t =>
            {
                var parts     = t.SourceTable.Split('.', 2);
                var schema    = parts.Length == 2 ? parts[0] : "dbo";
                var table     = parts.Length == 2 ? parts[1] : parts[0];
                var pkColumns = DeserializePkColumns(t.TriggerId, t.PkColumnsJson);
                return new TriggerApplyMetadata(schema, table, pkColumns, t.TriggerVersion);
            });
    }

    private static IReadOnlyList<string> DeserializePkColumns(string triggerId, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)
                   ?? throw new InvalidOperationException($"pk_columns_json for trigger {triggerId} deserializes to null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Malformed pk_columns_json for trigger {triggerId}: {json}", ex);
        }
    }
}
