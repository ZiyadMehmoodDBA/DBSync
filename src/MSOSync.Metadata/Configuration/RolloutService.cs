using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed class RolloutService(
    AppDbContext db,
    IConfigurationAssignmentService assignmentSvc,
    IAuditService auditSvc) : IRolloutService
{
    public async Task<RolloutDto> StartRolloutAsync(StartRolloutRequest req, Guid userId, CancellationToken ct)
    {
        var rolloutId     = Guid.NewGuid();
        var correlationId = rolloutId.ToString();
        var now           = DateTime.UtcNow;

        var rollout = new SyncConfigurationRollout
        {
            Id              = rolloutId,
            Status          = "InProgress",
            TemplateId      = req.TemplateId,
            TemplateVersion = req.TemplateVersion,
            TargetNodeCount = req.NodeIds.Count,
            PendingCount    = req.NodeIds.Count,
            InitiatedBy     = userId,
            StartedAt       = now,
        };
        db.ConfigurationRollouts.Add(rollout);
        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(ConfigurationAuditConstants.RolloutStarted,
            $"Rollout {rolloutId} started for template {req.TemplateId} v{req.TemplateVersion} ({req.NodeIds.Count} nodes)",
            userId.ToString(), ct);

        // Process each node — failure policy: continue on per-node failure
        int applied = 0, failed = 0;
        foreach (var nodeId in req.NodeIds)
        {
            try
            {
                await assignmentSvc.AssignAsync(nodeId, req.TemplateId, req.TemplateVersion,
                    userId, correlationId, ct);
                applied++;
            }
            catch
            {
                failed++;
            }

            rollout.AppliedCount    = applied;
            rollout.FailedCount     = failed;
            rollout.PendingCount    = req.NodeIds.Count - applied - failed;
            rollout.ProgressPercent = (applied + failed) * 100 / req.NodeIds.Count;
            await db.SaveChangesAsync(ct);
        }

        rollout.Status      = "Completed";
        rollout.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return MapRollout(rollout);
    }

    public async Task<RolloutDto> GetRolloutAsync(Guid rolloutId, CancellationToken ct)
    {
        var rollout = await db.ConfigurationRollouts.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rolloutId, ct)
            ?? throw new NotFoundException($"Rollout {rolloutId} not found");
        return MapRollout(rollout);
    }

    private static RolloutDto MapRollout(SyncConfigurationRollout r) => new(
        r.Id, r.Status, r.TemplateId, r.TemplateVersion,
        r.TargetNodeCount, r.AppliedCount, r.FailedCount, r.PendingCount,
        r.ProgressPercent, r.InitiatedBy, r.StartedAt, r.CompletedAt);
}
