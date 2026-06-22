using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Dtos.Batches;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/batches")]
public sealed class BatchController(
    AppDbContext db,
    IBatchStateMachine stateMachine,
    RetryProcessor retryProcessor,
    ICurrentUserService currentUser,
    IDatabaseLockProvider lockProvider) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetBatches([FromQuery] BatchListRequest req, CancellationToken ct)
    {
        var query = db.OutgoingBatches.AsNoTracking();

        if (!string.IsNullOrEmpty(req.NodeId))    query = query.Where(b => b.NodeId == req.NodeId);
        if (!string.IsNullOrEmpty(req.ChannelId)) query = query.Where(b => b.ChannelId == req.ChannelId);
        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<BatchStatus>(req.Status, ignoreCase: true, out var status))
            query = query.Where(b => b.Status == (byte)status);

        query = (req.SortBy, req.SortDirection.ToLowerInvariant()) switch
        {
            ("batchId",    "asc")  => query.OrderBy(b => b.BatchId),
            ("batchId",    _)      => query.OrderByDescending(b => b.BatchId),
            ("status",     "asc")  => query.OrderBy(b => b.Status),
            ("status",     _)      => query.OrderByDescending(b => b.Status),
            (_,            "asc")  => query.OrderBy(b => b.CreateTime),
            _                      => query.OrderByDescending(b => b.CreateTime),
        };

        var total      = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(total / (double)req.PageSize);
        var items      = await query
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var data = items.Select(b => new OutgoingBatchDto(
            b.BatchId, (BatchStatus)b.Status, b.NodeId, b.ChannelId,
            b.CreateTime, b.SentTime, b.AckTime, b.RetryCount, b.RowCount, null));

        return Ok(new { data, total, page = req.Page, pageSize = req.PageSize, totalPages });
    }

    [HttpGet("{batchId:long}")]
    [Authorize]
    public async Task<IActionResult> GetBatch(long batchId, CancellationToken ct)
    {
        var batch = await db.OutgoingBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchId == batchId, ct);
        if (batch == null) return NotFound();

        var error = await db.BatchErrors.AsNoTracking()
            .Where(e => e.BatchId == batchId)
            .OrderByDescending(e => e.ErrorId)
            .Select(e => e.ErrorMessage)
            .FirstOrDefaultAsync(ct);

        var dto = new OutgoingBatchDto(
            batch.BatchId, (BatchStatus)batch.Status, batch.NodeId, batch.ChannelId,
            batch.CreateTime, batch.SentTime, batch.AckTime, batch.RetryCount, batch.RowCount, error);

        return Ok(dto);
    }

    [HttpPost("{batchId:long}/retry")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> RetryBatch(long batchId, CancellationToken ct)
    {
        var batch = await db.OutgoingBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchId == batchId, ct);
        if (batch == null) return NotFound();

        var transitioned = await stateMachine.MoveToRetryAsync(batchId, ct);

        if (!transitioned)
            return Conflict(new { code = "INVALID_TRANSITION",
                message = $"Batch {batchId} is not in Error status" });

        return Ok();
    }

    [HttpPost("retry-all")]
    [Authorize(Policy = "OperatorOrAbove")]
    public async Task<IActionResult> RetryAll(CancellationToken ct)
    {
        var lease = await lockProvider.TryAcquireAsync(LockNames.RetryEngine, ct);
        if (lease == null)
            return Conflict(new { code = "LOCK_UNAVAILABLE", message = "Retry engine is currently running. Try again shortly." });

        await using (lease)
        {
            var count = await retryProcessor.ProcessAsync(ct);
            return Ok(new RetryAllResponse(count, DateTime.UtcNow, currentUser.GetCurrentUsername()));
        }
    }
}
