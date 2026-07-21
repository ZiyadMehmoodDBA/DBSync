using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Rolling;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Scheduler.Workers;

public sealed class RollingOperationWorker(
    IServiceScopeFactory            scopeFactory,
    IOptions<LifecycleOptions>      lifecycleOptions,
    ILogger<RollingOperationWorker> logger,
    IWorkerStatusRegistry           registry) : BackgroundService
{
    private int _running;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.RollingWorkerIntervalSeconds > 0
            ? lifecycleOptions.Value.RollingWorkerIntervalSeconds : 15);
        registry.Register(nameof(RollingOperationWorker), interval);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.RollingWorkerIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                logger.LogWarning("RollingOperationWorker tick skipped — previous tick still running");
                continue;
            }
            registry.RecordTickStart(nameof(RollingOperationWorker));
            try
            {
                await RunTickAsync(ct);
                registry.RecordTickComplete(nameof(RollingOperationWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(RollingOperationWorker), ex);
                logger.LogError(ex, "RollingOperationWorker tick failed");
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<INodeLifecycleService>();
        var history   = scope.ServiceProvider.GetRequiredService<INodeLifecycleHistoryService>();
        var clock     = scope.ServiceProvider.GetRequiredService<IClock>();

        await DetectDrainCompletionsAsync(db, history, clock, ct);
        await AdvanceRollingOperationsAsync(db, lifecycle, clock, ct);
    }

    private static async Task DetectDrainCompletionsAsync(
        AppDbContext db, INodeLifecycleHistoryService history, IClock clock, CancellationToken ct)
    {
        var draining = await db.Nodes
            .Where(n => n.LifecycleState == NodeLifecycleState.Draining && n.DrainCompletedAt == null)
            .ToListAsync(ct);

        foreach (var node in draining)
        {
            var open = await db.OutgoingBatches.CountAsync(
                b => b.NodeId == node.NodeId && b.Status != 2, ct);
            if (open > 0) continue;

            node.DrainCompletedAt = clock.UtcNow;
            await history.WriteTransitionAsync(new LifecycleTransitionRecord(
                node.NodeId, NodeLifecycleState.Draining, NodeLifecycleState.Draining,
                LifecycleTrigger.System, NodeManagementAuditActions.NodeDrainCompleted,
                "system", Guid.NewGuid()), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static readonly string[] RunningStatuses = ["Running", "Paused"];
    private static readonly string[] RollingOpTypes  = ["RollingMaintenance", "RollingUpgrade"];
    private static readonly string[] NonTerminalStep  =
    [
        nameof(RollingStepStatus.Pending), nameof(RollingStepStatus.Draining),
        nameof(RollingStepStatus.InMaintenance), nameof(RollingStepStatus.AwaitingVerification),
    ];

    private static async Task AdvanceRollingOperationsAsync(
        AppDbContext db, INodeLifecycleService lifecycle, IClock clock, CancellationToken ct)
    {
        var ops = await db.Operations
            .Where(o => o.Status == "Running" && RollingOpTypes.Contains(o.OperationType))
            .ToListAsync(ct);

        foreach (var op in ops)
        {
            var steps = await db.OperationSteps
                .Where(s => s.OperationId == op.OperationId)
                .ToListAsync(ct);

            if (steps.Count == 0) continue;

            var policy = RollingOperationPolicy.FromJson(op.MetadataJson!);
            var now    = clock.UtcNow;

            var activeWave = steps
                .Where(s => NonTerminalStep.Contains(s.Status))
                .Select(s => (int?)s.WaveNumber)
                .Min();

            if (activeWave is null)
            {
                // all steps terminal — complete the operation
                CompleteOperation(op, steps, now);
                await db.SaveChangesAsync(ct);
                continue;
            }

            var activeWaveNum = activeWave.Value;

            // Health gate: check previous wave
            if (activeWaveNum > 1)
            {
                var prevWaveSteps = steps.Where(s => s.WaveNumber == activeWaveNum - 1).ToList();
                var prevNodeIds   = prevWaveSteps.Select(s => s.NodeId).ToList();
                var prevNodes     = await db.Nodes.AsNoTracking()
                    .Where(n => prevNodeIds.Contains(n.NodeId)).ToListAsync(ct);

                var unhealthy = prevNodes
                    .Where(n => n.LifecycleState != NodeLifecycleState.Active
                             || n.MaintenanceMode
                             || n.ConnectivityStatus != ConnectivityStatus.Reachable)
                    .Select(n => n.NodeId).ToList();

                if (unhealthy.Count > 0)
                {
                    op.Status = "Paused";
                    op.ProgressMessage = $"Health gate failed: {string.Join(", ", unhealthy)}";
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                // Soak gate: check if previous wave completed within soak period
                var prevMaxCompleted = prevWaveSteps
                    .Where(s => s.CompletedAt.HasValue)
                    .Select(s => s.CompletedAt!.Value)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();

                if (prevMaxCompleted != DateTime.MinValue
                    && (now - prevMaxCompleted).TotalSeconds < policy.GateSoakSeconds)
                {
                    // Within soak: skip starting new Pending steps, but still advance in-flight
                    await AdvanceActiveWaveStepsAsync(db, lifecycle, clock, op, steps, policy,
                        activeWaveNum, startPending: false, ct);
                    await db.SaveChangesAsync(ct);
                    continue;
                }
            }

            await AdvanceActiveWaveStepsAsync(db, lifecycle, clock, op, steps, policy,
                activeWaveNum, startPending: true, ct);

            // Update progress
            var total     = steps.Count;
            var completed = steps.Count(s => s.Status == nameof(RollingStepStatus.Completed));
            op.ProgressPercent  = total > 0 ? completed * 100 / total : 0;
            var maxWave = steps.Max(s => s.WaveNumber);
            op.ProgressMessage = $"Wave {activeWaveNum}/{maxWave}";

            // Check if now complete (skip if this tick just paused the op)
            if (op.Status == "Running" && steps.All(s => !NonTerminalStep.Contains(s.Status)))
                CompleteOperation(op, steps, now);

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task AdvanceActiveWaveStepsAsync(
        AppDbContext db, INodeLifecycleService lifecycle, IClock clock,
        SyncOperation op, List<SyncOperationStep> steps, RollingOperationPolicy policy,
        int waveNumber, bool startPending, CancellationToken ct)
    {
        var now        = clock.UtcNow;
        var isUpgrade  = op.OperationType == "RollingUpgrade";
        var waveSteps  = steps.Where(s => s.WaveNumber == waveNumber).ToList();

        foreach (var step in waveSteps)
        {
            switch (Enum.Parse<RollingStepStatus>(step.Status))
            {
                case RollingStepStatus.Pending when startPending:
                    await lifecycle.StartDrainAsync(step.NodeId, "Rolling operation", "system", ct);
                    step.Status    = nameof(RollingStepStatus.Draining);
                    step.StartedAt = now;
                    break;

                case RollingStepStatus.Draining:
                    var drainingNode = await db.Nodes.AsNoTracking()
                        .FirstOrDefaultAsync(n => n.NodeId == step.NodeId, ct);
                    if (drainingNode?.DrainCompletedAt != null)
                    {
                        await lifecycle.StartMaintenanceAsync(
                            step.NodeId, "Rolling operation",
                            policy.WaveAction == "auto-window" && policy.WindowSeconds.HasValue
                                ? DateTimeOffset.UtcNow.AddSeconds(policy.WindowSeconds.Value)
                                : (DateTimeOffset?)null,
                            notifyNode: false, "system", ct);
                        step.Status = nameof(RollingStepStatus.InMaintenance);
                    }
                    break;

                case RollingStepStatus.InMaintenance:
                    if (policy.WaveAction == "auto-window" && policy.WindowSeconds.HasValue
                        && step.StartedAt.HasValue
                        && (now - step.StartedAt.Value).TotalSeconds >= policy.WindowSeconds.Value)
                    {
                        step.Status = nameof(RollingStepStatus.AwaitingVerification);
                    }
                    break;

                case RollingStepStatus.AwaitingVerification:
                    if (isUpgrade)
                    {
                        var upgradeNode = await db.Nodes.AsNoTracking()
                            .FirstOrDefaultAsync(n => n.NodeId == step.NodeId, ct);
                        if (upgradeNode?.AgentVersion == policy.TargetVersion)
                        {
                            await FinishStepAsync(step, lifecycle, upgradeNode, now, ct);
                        }
                        else if (step.StartedAt.HasValue
                            && (now - step.StartedAt.Value).TotalSeconds > policy.VerificationTimeoutSeconds)
                        {
                            step.Status       = nameof(RollingStepStatus.Failed);
                            step.ErrorMessage = "Verification timeout";
                            step.CompletedAt  = now;
                            op.Status         = "Paused";
                            op.ProgressMessage = $"Verification timeout for node {step.NodeId}";
                        }
                    }
                    else
                    {
                        var maintNode = await db.Nodes.AsNoTracking()
                            .FirstOrDefaultAsync(n => n.NodeId == step.NodeId, ct);
                        await FinishStepAsync(step, lifecycle, maintNode, now, ct);
                    }
                    break;
            }
        }
    }

    private static async Task FinishStepAsync(
        SyncOperationStep step, INodeLifecycleService lifecycle,
        SyncNode? node, DateTime now, CancellationToken ct)
    {
        if (node?.MaintenanceMode == true)
            await lifecycle.EndMaintenanceAsync(step.NodeId, "system", ct);

        var currentNode = node;
        if (currentNode?.LifecycleState == NodeLifecycleState.Draining
            || currentNode?.LifecycleState == NodeLifecycleState.Active)
        {
            if (currentNode.LifecycleState == NodeLifecycleState.Draining)
                await lifecycle.ResumeFromDrainAsync(step.NodeId, "Rolling operation complete", "system", ct);
        }

        step.Status      = nameof(RollingStepStatus.Completed);
        step.CompletedAt = now;
    }

    private static void CompleteOperation(SyncOperation op, List<SyncOperationStep> steps, DateTime now)
    {
        var hasFailures = steps.Any(s => s.Status == nameof(RollingStepStatus.Failed));
        op.Status         = nameof(OperationStatus.Completed);
        op.Result         = hasFailures ? nameof(OperationResult.PartialSuccess) : nameof(OperationResult.Success);
        op.CompletedAt    = now;
        op.ProgressPercent = 100;
    }
}
