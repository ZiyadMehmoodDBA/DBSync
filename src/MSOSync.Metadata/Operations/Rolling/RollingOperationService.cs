using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Rolling;

public sealed class RollingOperationService(
    AppDbContext          db,
    IOperationService     operations,
    INodeLifecycleService lifecycle) : IRollingOperationService
{
    private static readonly string[] NonTerminalStepStatuses =
        [nameof(RollingStepStatus.Pending), nameof(RollingStepStatus.Draining),
         nameof(RollingStepStatus.InMaintenance), nameof(RollingStepStatus.AwaitingVerification)];

    public async Task<Guid> CreateAsync(OperationType kind, IReadOnlyList<string> nodeIds,
        RollingOperationPolicy policy, Guid? initiatedBy, string actor, CancellationToken ct = default)
    {
        if (kind is not (OperationType.RollingMaintenance or OperationType.RollingUpgrade))
            throw new OperationStateException($"Unsupported rolling operation type {kind}");
        if (nodeIds.Count == 0)
            throw new OperationStateException("Node list is empty");
        if (kind == OperationType.RollingUpgrade && string.IsNullOrWhiteSpace(policy.TargetVersion))
            throw new OperationStateException("TargetVersion is required for rolling upgrades");

        var nodes = await db.Nodes.AsNoTracking()
            .Where(n => nodeIds.Contains(n.NodeId)).ToListAsync(ct);
        var missing = nodeIds.Except(nodes.Select(n => n.NodeId)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException($"Nodes not found: {string.Join(", ", missing)}", "NODE_NOT_FOUND");
        var notActive = nodes.Where(n => n.LifecycleState != NodeLifecycleState.Active).ToList();
        if (notActive.Count > 0)
            throw new OperationStateException(
                $"Nodes not Active: {string.Join(", ", notActive.Select(n => n.NodeId))}");

        var busy = await db.OperationSteps.AsNoTracking()
            .Where(s => nodeIds.Contains(s.NodeId) && NonTerminalStepStatuses.Contains(s.Status))
            .Select(s => s.NodeId).Distinct().ToListAsync(ct);
        if (busy.Count > 0)
            throw new OperationStateException(
                $"Nodes already in a rolling operation: {string.Join(", ", busy)}");

        var waveSize = policy.WaveSize
            ?? Math.Max(1, (int)Math.Ceiling(nodeIds.Count * (policy.WavePercent ?? 100) / 100.0));

        var operationId = await operations.CreateAsync(
            kind, referenceId: null, initiatedBy, OperationSource.User,
            correlationId: Guid.NewGuid().ToString(),
            canCancel: true, canRetry: false,
            summary: $"{kind} across {nodeIds.Count} node(s), wave size {waveSize}, by {actor}",
            metadataJson: RollingOperationPolicy.ToJson(policy), ct);

        var wave = 0;
        foreach (var chunk in nodeIds.Chunk(waveSize))
        {
            wave++;
            foreach (var nodeId in chunk)
                db.OperationSteps.Add(new SyncOperationStep
                {
                    StepId = Guid.NewGuid(), OperationId = operationId, NodeId = nodeId,
                    WaveNumber = wave, Status = nameof(RollingStepStatus.Pending),
                });
        }
        await db.SaveChangesAsync(ct);
        return operationId;
    }

    public async Task PauseAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await RequireOpAsync(operationId, ct);
        if (op.Status != nameof(OperationStatus.Running))
            throw new OperationStateException($"Cannot pause operation in status {op.Status}");
        op.Status = "Paused";
        await db.SaveChangesAsync(ct);
    }

    public async Task ResumeAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await RequireOpAsync(operationId, ct);
        if (op.Status != "Paused")
            throw new OperationStateException($"Cannot resume operation in status {op.Status}");
        op.Status = nameof(OperationStatus.Running);
        await db.SaveChangesAsync(ct);
    }

    public async Task AbortAsync(Guid operationId, string actor, CancellationToken ct = default)
    {
        var op = await RequireOpAsync(operationId, ct);
        if (op.Status is not (nameof(OperationStatus.Pending) or nameof(OperationStatus.Running) or "Paused"))
            throw new OperationStateException($"Cannot abort operation in status {op.Status}");

        var steps = await db.OperationSteps
            .Where(s => s.OperationId == operationId).ToListAsync(ct);
        foreach (var step in steps)
        {
            switch (Enum.Parse<RollingStepStatus>(step.Status))
            {
                case RollingStepStatus.Pending:
                    step.Status = nameof(RollingStepStatus.Skipped);
                    step.CompletedAt = DateTime.UtcNow;
                    break;
                case RollingStepStatus.Draining:
                case RollingStepStatus.InMaintenance:
                case RollingStepStatus.AwaitingVerification:
                    await RestoreNodeAsync(step.NodeId, actor, ct);
                    step.Status = nameof(RollingStepStatus.Skipped);
                    step.CompletedAt = DateTime.UtcNow;
                    break;
            }
        }
        op.Status = nameof(OperationStatus.Cancelled);
        op.Result = nameof(OperationResult.Cancelled);
        op.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmStepAsync(Guid stepId, CancellationToken ct = default)
    {
        var step = await db.OperationSteps.FirstOrDefaultAsync(s => s.StepId == stepId, ct)
            ?? throw new NotFoundException($"Step {stepId} not found", "STEP_NOT_FOUND");
        if (step.Status != nameof(RollingStepStatus.InMaintenance))
            throw new OperationStateException($"Cannot confirm step in status {step.Status}");
        step.Status = nameof(RollingStepStatus.AwaitingVerification);
        await db.SaveChangesAsync(ct);
    }

    private async Task<SyncOperation> RequireOpAsync(Guid id, CancellationToken ct)
        => await db.Operations.FirstOrDefaultAsync(o => o.OperationId == id, ct)
           ?? throw new NotFoundException($"Operation {id} not found", "OPERATION_NOT_FOUND");

    private async Task RestoreNodeAsync(string nodeId, string actor, CancellationToken ct)
    {
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node is null) return;
        if (node.MaintenanceMode)
            await lifecycle.EndMaintenanceAsync(nodeId, actor, ct);
        if (node.LifecycleState == NodeLifecycleState.Draining)
            await lifecycle.ResumeFromDrainAsync(nodeId, "Rolling operation aborted", actor, ct);
    }
}
