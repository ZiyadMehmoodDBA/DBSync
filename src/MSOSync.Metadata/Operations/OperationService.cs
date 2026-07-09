using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations;

public sealed class OperationService(
    AppDbContext             db,
    IPublisher               publisher,
    IKeyedServiceProvider    keyedServices) : IOperationService
{
    public async Task<Guid> CreateAsync(
        OperationType   type,
        Guid?           referenceId,
        Guid?           initiatedBy,
        OperationSource source,
        string          correlationId,
        bool            canCancel,
        bool            canRetry,
        string          summary,
        string?         metadataJson,
        CancellationToken ct)
    {
        var op = new SyncOperation
        {
            OperationId     = Guid.NewGuid(),
            OperationType   = type.ToString(),
            ReferenceId     = referenceId,
            Status          = OperationStatus.Pending.ToString(),
            Result          = null,
            Source          = source.ToString(),
            ProgressPercent = null,
            ProgressMessage = null,
            CorrelationId   = correlationId,
            InitiatedBy     = initiatedBy,
            MetadataJson    = metadataJson,
            Summary         = summary,
            CanCancel       = canCancel,
            CanRetry        = canRetry,
            StartedAt       = DateTime.UtcNow,
            CompletedAt     = null,
        };

        db.Operations.Add(op);
        await db.SaveChangesAsync(ct);

        await publisher.Publish(
            new OperationChangedEvent(op.OperationId, op.OperationType, op.Status), ct);

        return op.OperationId;
    }

    public async Task UpdateProgressAsync(
        Guid operationId, int percent, string? message, CancellationToken ct)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
            ?? throw new NotFoundException($"Operation {operationId} not found.");

        await db.Operations
            .Where(o => o.OperationId == operationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ProgressPercent,  percent)
                .SetProperty(o => o.ProgressMessage,  message)
                .SetProperty(o => o.Status,           OperationStatus.Running.ToString()),
            ct);

        db.ChangeTracker.Clear();
        await PublishChangedAsync(operationId, ct);
    }

    public async Task CompleteAsync(
        Guid operationId, OperationResult result, string? summary, CancellationToken ct)
    {
        var resultStr = result.ToString();
        var status    = result == OperationResult.Success || result == OperationResult.PartialSuccess
            ? OperationStatus.Completed.ToString()
            : OperationStatus.Failed.ToString();

        if (summary is not null)
        {
            var finalSummary = summary;
            await db.Operations
                .Where(o => o.OperationId == operationId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status,          status)
                    .SetProperty(o => o.Result,          resultStr)
                    .SetProperty(o => o.CompletedAt,     DateTime.UtcNow)
                    .SetProperty(o => o.ProgressPercent, 100)
                    .SetProperty(o => o.Summary,         finalSummary),
                ct);
        }
        else
        {
            await db.Operations
                .Where(o => o.OperationId == operationId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status,          status)
                    .SetProperty(o => o.Result,          resultStr)
                    .SetProperty(o => o.CompletedAt,     DateTime.UtcNow)
                    .SetProperty(o => o.ProgressPercent, 100),
                ct);
        }

        db.ChangeTracker.Clear();
        await PublishChangedAsync(operationId, ct);
    }

    public async Task CancelAsync(Guid operationId, Guid actorId, CancellationToken ct)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
            ?? throw new NotFoundException($"Operation {operationId} not found.");

        if (op.Status is not ("Pending" or "Running"))
            throw new InvalidOperationException(
                $"Operation {operationId} is in status '{op.Status}' and cannot be cancelled.");

        if (!op.CanCancel)
            throw new InvalidOperationException(
                $"Operation {operationId} does not support cancellation.");

        // Delegate domain-side cancellation first
        if (Enum.TryParse<OperationType>(op.OperationType, out var opType))
        {
            var handler = keyedServices.GetKeyedService<IOperationHandler>(opType);
            if (handler is not null)
            {
                if (op.ReferenceId.HasValue)
                {
                    await handler.CancelAsync(op.ReferenceId.Value, actorId, ct);
                }
                else if (!string.IsNullOrEmpty(op.CorrelationId)
                         && handler is MSOSync.Metadata.Operations.Handlers.DecommissionOperationHandler decomHandler)
                {
                    // Decommission uses correlationId (nodeId string) instead of a Guid referenceId
                    await decomHandler.CancelByCorrelationAsync(op.CorrelationId, actorId, ct);
                }
            }
        }

        await db.Operations
            .Where(o => o.OperationId == operationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status,      OperationStatus.Cancelled.ToString())
                .SetProperty(o => o.Result,      OperationResult.Cancelled.ToString())
                .SetProperty(o => o.CompletedAt, DateTime.UtcNow),
            ct);

        db.ChangeTracker.Clear();
        await PublishChangedAsync(operationId, ct);
    }

    public async Task RetryAsync(Guid operationId, Guid actorId, CancellationToken ct)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
            ?? throw new NotFoundException($"Operation {operationId} not found.");

        if (op.Status is not ("Failed" or "Cancelled"))
            throw new InvalidOperationException(
                $"Operation {operationId} is in status '{op.Status}' and cannot be retried.");

        if (!op.CanRetry)
            throw new InvalidOperationException(
                $"Operation {operationId} does not support retry.");

        // Delegate domain-side retry first
        if (op.ReferenceId.HasValue
            && Enum.TryParse<OperationType>(op.OperationType, out var opType))
        {
            var handler = keyedServices.GetKeyedService<IOperationHandler>(opType);
            if (handler is not null)
                await handler.RetryAsync(op.ReferenceId.Value, actorId, ct);
        }

        // Reset to Pending so the domain worker can pick it up
        await db.Operations
            .Where(o => o.OperationId == operationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status,          OperationStatus.Pending.ToString())
                .SetProperty(o => o.Result,          (string?)null)
                .SetProperty(o => o.CompletedAt,     (DateTime?)null)
                .SetProperty(o => o.ProgressPercent, (int?)null)
                .SetProperty(o => o.ProgressMessage, (string?)null),
            ct);

        db.ChangeTracker.Clear();
        await PublishChangedAsync(operationId, ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task PublishChangedAsync(Guid operationId, CancellationToken ct)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct);
        if (op is null) return;

        await publisher.Publish(
            new OperationChangedEvent(op.OperationId, op.OperationType, op.Status), ct);
    }
}
