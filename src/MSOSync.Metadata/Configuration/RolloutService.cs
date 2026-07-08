using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed class RolloutService(
    AppDbContext db,
    IAuditService auditSvc,
    IServiceScopeFactory scopeFactory) : IRolloutService
{
    public async Task<RolloutDto> StartRolloutAsync(
        Guid templateId, int version, IReadOnlyList<string> nodeIds,
        Guid actorId, CancellationToken ct)
    {
        var rolloutId     = Guid.NewGuid();
        var correlationId = rolloutId.ToString();
        var now           = DateTime.UtcNow;

        // Persist rollout record first
        var rollout = new SyncConfigurationRollout
        {
            Id              = rolloutId,
            Status          = "InProgress",
            TemplateId      = templateId,
            TemplateVersion = version,
            TargetNodeCount = nodeIds.Count,
            PendingCount    = nodeIds.Count,
            InitiatedBy     = actorId,
            StartedAt       = now,
        };
        db.ConfigurationRollouts.Add(rollout);
        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(ConfigurationAuditConstants.RolloutStarted,
            $"Rollout {rolloutId} started for template {templateId} v{version} ({nodeIds.Count} nodes)",
            actorId.ToString(), ct);

        // Fire and forget — do not await; use a new scope for background DB work
        var capturedNodeIds    = nodeIds.ToList();
        var capturedTemplateId = templateId;
        var capturedVersion    = version;
        var capturedActor      = actorId;
        var capturedCorrelation = correlationId;
        var capturedRolloutId  = rolloutId;
        var capturedTotal      = nodeIds.Count;

        _ = Task.Run(async () =>
        {
            await using var scope     = scopeFactory.CreateAsyncScope();
            var bgDb                  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bgAssignmentSvc       = scope.ServiceProvider.GetRequiredService<IConfigurationAssignmentService>();

            int succeeded = 0, failed = 0;
            foreach (var nodeId in capturedNodeIds)
            {
                try
                {
                    await bgAssignmentSvc.AssignAsync(nodeId, capturedTemplateId, capturedVersion,
                        capturedActor, capturedCorrelation, CancellationToken.None);
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    failed++;
                }

                var bgRollout = await bgDb.ConfigurationRollouts.FindAsync(capturedRolloutId);
                if (bgRollout is not null)
                {
                    bgRollout.AppliedCount    = succeeded;
                    bgRollout.FailedCount     = failed;
                    bgRollout.PendingCount    = capturedTotal - succeeded - failed;
                    bgRollout.ProgressPercent = (succeeded + failed) * 100 / capturedTotal;
                    await bgDb.SaveChangesAsync(CancellationToken.None);
                }
            }

            var finalRollout = await bgDb.ConfigurationRollouts.FindAsync(capturedRolloutId);
            if (finalRollout is not null)
            {
                finalRollout.Status      = "Completed";
                finalRollout.CompletedAt = DateTime.UtcNow;
                await bgDb.SaveChangesAsync(CancellationToken.None);
            }
        }, CancellationToken.None); // CancellationToken.None — background work continues after response

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
