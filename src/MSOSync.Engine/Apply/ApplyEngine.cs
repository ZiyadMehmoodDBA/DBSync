// src/MSOSync.Engine/Apply/ApplyEngine.cs
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public sealed class ApplyEngine(
    AppDbContext                 db,
    ISqlConnectionFactory        connectionFactory,
    ISqlEventApplicator          applicator,
    IApplyFailureClassifier      classifier,
    ITriggerApplyMetadataService metadataService,
    IClock                       clock,
    ILogger<ApplyEngine>         logger) : IApplyService
{
    public async Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default)
    {
        // Attach entity if not already tracked so SaveChangesAsync persists status updates.
        if (db.Entry(incoming).State == EntityState.Detached)
            db.Attach(incoming);

        incoming.Status = IncomingBatchStatus.Applying;
        await db.SaveChangesAsync(ct);

        var triggerIds = payload.Events.Select(e => e.TriggerId).Distinct().ToList();
        var metadata   = await metadataService.GetMetadataAsync(triggerIds, ct);

        int appliedRows = 0;
        int errorRows   = 0;
        string? lastError = null;
        bool fatalError = false;

        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            var context = new ApplyContext(connection, transaction, metadata);
            int idx = 0;
            foreach (var evt in payload.Events)
            {
                var (ok, err) = await ApplyEventAsync(evt, context, idx++, ct);
                if (ok) appliedRows++;
                else { errorRows++; lastError = err; }
            }
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            fatalError = true;
            errorRows  = payload.Events.Count;
            appliedRows = 0;
            lastError  = ex.Message;
            logger.LogError(ex, "ApplyEngine: fatal error on batch {BatchId}", incoming.BatchId);
        }
        finally
        {
            incoming.Status = fatalError || (appliedRows == 0 && errorRows > 0)
                ? IncomingBatchStatus.Error
                : errorRows > 0
                    ? IncomingBatchStatus.PartialSuccess
                    : IncomingBatchStatus.Applied;
            incoming.AppliedTime = clock.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return new ApplyResult(errorRows == 0, appliedRows, errorRows, errorRows == 0 ? null : lastError);
    }

    private async Task<(bool ok, string? error)> ApplyEventAsync(
        EventPayload evt, ApplyContext ctx, int eventIndex, CancellationToken ct)
    {
        if (!ctx.Metadata.TryGetValue(evt.TriggerId, out var meta))
        {
            logger.LogWarning("ApplyEngine: no metadata for trigger {TriggerId}", evt.TriggerId);
            return (false, ApplyFailureCategory.MetadataMissing.ToString());
        }

        if (meta.TriggerVersion < 2)
        {
            logger.LogWarning("ApplyEngine: trigger {TriggerId} version {V} < 2", evt.TriggerId, meta.TriggerVersion);
            return (false, ApplyFailureCategory.MetadataMissing.ToString());
        }

        SqlStatement stmt;
        try
        {
            stmt = BuildStatement(evt, meta);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ApplyEngine: SQL build error on event {EventId}", evt.EventId);
            return (false, ApplyFailureCategory.SerializationError.ToString());
        }

        var sp = $"sp_{eventIndex}";
        ctx.Transaction.Save(sp);

        try
        {
            await using var cmd = ctx.Connection.CreateCommand();
            cmd.CommandText = stmt.CommandText;
            cmd.Transaction = ctx.Transaction;
            foreach (var p in stmt.Parameters)
                cmd.Parameters.Add(p);

            var rows = await cmd.ExecuteNonQueryAsync(ct);

            if (rows == 0 && evt.EventType != "INSERT")
            {
                ctx.Transaction.Rollback(sp);
                logger.LogWarning("ApplyEngine: RowNotFound for event {EventId}", evt.EventId);
                return (false, ApplyFailureCategory.RowNotFound.ToString());
            }

            return (true, null);
        }
        catch (SqlException sqlEx)
        {
            var cat = classifier.Classify(sqlEx.Number);
            if (cat is ApplyFailureCategory.DuplicateKey
                    or ApplyFailureCategory.FKViolation
                    or ApplyFailureCategory.RowNotFound)
            {
                ctx.Transaction.Rollback(sp);
                logger.LogWarning(sqlEx, "ApplyEngine: row-level {Cat} on event {EventId}", cat, evt.EventId);
                return (false, cat.ToString());
            }
            throw;
        }
    }

    private SqlStatement BuildStatement(EventPayload evt, TriggerApplyMetadata meta)
    {
        JsonElement? pkData = null;
        JsonElement? rowData = null;

        if (evt.PkData != null)
        {
            using var pkDoc = JsonDocument.Parse(evt.PkData);
            pkData = pkDoc.RootElement.Clone();
        }
        if (evt.RowData != null)
        {
            using var rowDoc = JsonDocument.Parse(evt.RowData);
            rowData = rowDoc.RootElement.Clone();
        }

        return evt.EventType switch
        {
            "INSERT" => rowData.HasValue
                ? applicator.BuildInsert(meta.SchemaName, meta.TableName, rowData.Value)
                : throw new InvalidOperationException("INSERT event missing row_data"),
            "UPDATE" => pkData.HasValue && rowData.HasValue
                ? applicator.BuildUpdate(meta.SchemaName, meta.TableName, pkData.Value, rowData.Value)
                : throw new InvalidOperationException("UPDATE event missing pk_data or row_data"),
            "DELETE" => pkData.HasValue
                ? applicator.BuildDelete(meta.SchemaName, meta.TableName, pkData.Value)
                : throw new InvalidOperationException("DELETE event missing pk_data"),
            _ => throw new InvalidOperationException($"Unknown event type: {evt.EventType}")
        };
    }
}
