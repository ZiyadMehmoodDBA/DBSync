using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations.Rolling;

public sealed class RollingOperationQueryService(AppDbContext db) : IRollingOperationQueryService
{
    public async Task<RollingOperationDetailDto> GetDetailAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct)
            ?? throw new NotFoundException($"Operation {operationId} not found", "OPERATION_NOT_FOUND");

        var steps = await db.OperationSteps.AsNoTracking()
            .Where(s => s.OperationId == operationId)
            .OrderBy(s => s.WaveNumber).ThenBy(s => s.NodeId)
            .Select(s => new RollingStepDto(s.StepId, s.NodeId, s.WaveNumber, s.Status,
                s.StartedAt, s.CompletedAt, s.ErrorMessage))
            .ToListAsync(ct);

        return new RollingOperationDetailDto(op.OperationId, op.OperationType, op.Status, op.Result,
            RollingOperationPolicy.FromJson(op.MetadataJson!), steps);
    }
}
