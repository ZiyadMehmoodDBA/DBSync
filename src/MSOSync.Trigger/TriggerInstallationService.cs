// src/MSOSync.Trigger/TriggerInstallationService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public sealed class TriggerInstallationService(
    AppDbContext db,
    SqlServerTriggerBuilder builder,
    IConfiguration config,
    IClock clock,
    ILogger<TriggerInstallationService> logger) : ITriggerInstallationService
{
    private string NodeId => config["Node:Id"] ?? Environment.MachineName;

    public async Task<TriggerVerifyResult> InstallAsync(
        SyncTrigger trigger, string nodeId, CancellationToken ct = default)
    {
        var ddl = builder.BuildDdl(trigger, nodeId);
        await db.Database.ExecuteSqlRawAsync(ddl, ct);

        trigger.TriggerVersion++;
        trigger.LastVerifiedTime = clock.UtcNow;
        db.TriggerHists.Add(new SyncTriggerHist
        {
            TriggerId = trigger.TriggerId,
            DdlText = ddl,
            TriggerVersion = trigger.TriggerVersion,
            CreateTime = clock.UtcNow
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Trigger {TriggerId} installed v{Version}", trigger.TriggerId, trigger.TriggerVersion);
        return new TriggerVerifyResult(trigger.TriggerId, nodeId, TriggerDriftStatus.Valid,
            trigger.TriggerVersion, trigger.TriggerVersion, null);
    }

    public async Task DropAsync(string triggerId, CancellationToken ct = default)
    {
        var triggerName = builder.GetTriggerName(triggerId);
        var sql = string.Concat(
            "IF OBJECT_ID(N'[", triggerName, "]', N'TR') IS NOT NULL DROP TRIGGER [", triggerName, "]");
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        logger.LogInformation("Trigger {TriggerId} dropped", triggerId);
    }

    public async Task<TriggerVerifyResult> RebuildAsync(string triggerId, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FirstOrDefaultAsync(t => t.TriggerId == triggerId, ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");
        return await InstallAsync(trigger, NodeId, ct);
    }
}
