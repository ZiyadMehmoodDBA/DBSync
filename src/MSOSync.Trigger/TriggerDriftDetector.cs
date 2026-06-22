// src/MSOSync.Trigger/TriggerDriftDetector.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;

namespace MSOSync.Trigger;

public sealed class TriggerDriftDetector(
    AppDbContext db,
    SqlServerTriggerBuilder builder,
    IConfiguration config,
    ILogger<TriggerDriftDetector> logger) : ITriggerDriftDetector
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    private string NodeId => config["Node:Id"] ?? Environment.MachineName;

    public async Task DetectAllAsync(CancellationToken ct = default)
    {
        var triggers = await db.Triggers.AsNoTracking()
            .Where(t => t.Enabled)
            .ToListAsync(ct);

        foreach (var t in triggers)
        {
            var result = await VerifyAsync(t.TriggerId, ct);
            if (result.Status != TriggerDriftStatus.Valid)
                logger.LogWarning("Trigger {TriggerId} status={Status}", t.TriggerId, result.Status);
        }
    }

    public async Task<TriggerVerifyResult> VerifyAsync(string triggerId, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FirstOrDefaultAsync(t => t.TriggerId == triggerId, ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");

        var nodeId = NodeId;
        var triggerName = builder.GetTriggerName(triggerId);

        // Query actual DDL from sys.sql_modules
        var installedDdl = await db.Database
            .SqlQueryRaw<string>(
                $"SELECT m.definition " +
                $"FROM sys.sql_modules m " +
                $"JOIN sys.triggers t ON t.object_id = m.object_id " +
                $"WHERE t.name = N'{triggerName}'")
            .FirstOrDefaultAsync(ct);

        if (installedDdl == null)
        {
            await UpdateLastVerified(trigger, ct);
            return new TriggerVerifyResult(triggerId, nodeId, TriggerDriftStatus.Missing,
                null, trigger.TriggerVersion, "Trigger not found in sys.triggers");
        }

        var expectedDdl = builder.BuildDdl(trigger, nodeId);
        var status = NormalizeDdl(installedDdl) == NormalizeDdl(expectedDdl)
            ? TriggerDriftStatus.Valid
            : TriggerDriftStatus.Drift;

        await UpdateLastVerified(trigger, ct);
        return new TriggerVerifyResult(triggerId, nodeId, status,
            trigger.TriggerVersion, trigger.TriggerVersion,
            status == TriggerDriftStatus.Drift ? "Installed DDL differs from expected" : null);
    }

    private async Task UpdateLastVerified(Persistence.Entities.SyncTrigger trigger, CancellationToken ct)
    {
        trigger.LastVerifiedTime = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeDdl(string ddl) =>
        string.Join(' ', ddl.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
              .ToUpperInvariant();
}
