using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;

namespace MSOSync.Metadata.BatchErrors;

public sealed class BatchErrorQueryService(
    AppDbContext             db,
    IErrorSeverityClassifier classifier) : IBatchErrorQueryService
{
    public async Task<PagedResult<BatchErrorSummaryDto>> GetBatchErrorsAsync(
        BatchErrorFilter filter, CancellationToken ct = default)
    {
        var q = db.BatchErrors.AsNoTracking();

        if (filter.BatchId      is not null) q = q.Where(e => e.BatchId      == filter.BatchId);
        if (filter.ConflictType is not null) q = q.Where(e => e.ConflictType == filter.ConflictType);
        if (filter.From         is not null) q = q.Where(e => e.CreateTime   >= filter.From);
        if (filter.To           is not null) q = q.Where(e => e.CreateTime   <= filter.To);

        if (filter.Severity is not null)
        {
            var types = classifier.GetConflictTypes(filter.Severity.Value);
            if (filter.Severity.Value == ErrorSeverity.Critical)
                q = q.Where(e => e.ConflictType == null || types.Contains(e.ConflictType));
            else
                q = q.Where(e => types.Contains(e.ConflictType));
        }

        var total = await q.CountAsync(ct);

        // Two-step projection: SQL pulls minimal columns, C# derives Severity
        var rawItems = await q
            .OrderByDescending(e => e.CreateTime)
            .Select(e => new
            {
                e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
                e.ErrorMessage, e.CreateTime, e.RetryCount
            })
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var items = rawItems
            .Select(e => new BatchErrorSummaryDto(
                e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
                classifier.Classify(e.ConflictType).ToString(),
                e.ErrorMessage, e.CreateTime, e.RetryCount))
            .ToList()
            .AsReadOnly();

        return new PagedResult<BatchErrorSummaryDto>(items, filter.Page, filter.PageSize, total);
    }

    public async Task<BatchErrorDetailDto?> GetBatchErrorByIdAsync(
        long errorId, CancellationToken ct = default)
    {
        var e = await db.BatchErrors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ErrorId == errorId, ct);

        if (e is null) return null;

        return new BatchErrorDetailDto(
            e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
            classifier.Classify(e.ConflictType).ToString(),
            e.ErrorMessage, e.CreateTime, e.RetryCount, e.LastRetryTime);
    }

    public async Task<BatchErrorSummaryCountDto> GetBatchErrorSummaryAsync(
        long? batchId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var baseQ = db.BatchErrors.AsNoTracking();
        if (batchId.HasValue) baseQ = baseQ.Where(e => e.BatchId    == batchId.Value);
        if (from.HasValue)    baseQ = baseQ.Where(e => e.CreateTime >= from.Value);
        if (to.HasValue)      baseQ = baseQ.Where(e => e.CreateTime <= to.Value);

        var infoTypes = classifier.GetConflictTypes(ErrorSeverity.Info);
        var warnTypes = classifier.GetConflictTypes(ErrorSeverity.Warning);
        var critTypes = classifier.GetConflictTypes(ErrorSeverity.Critical);

        int info = await baseQ.CountAsync(e => infoTypes.Contains(e.ConflictType), ct);
        int warn = await baseQ.CountAsync(e => warnTypes.Contains(e.ConflictType), ct);
        // Critical: null conflict_type is also Critical
        int crit = await baseQ.CountAsync(
            e => e.ConflictType == null || critTypes.Contains(e.ConflictType), ct);

        return new BatchErrorSummaryCountDto(info, warn, crit, info + warn + crit);
    }
}
