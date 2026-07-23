using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Batches;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Validators;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Locks;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.OutgoingBatches;
using MSOSync.Persistence.Lock;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/batches")]
public sealed class BatchController(
    IOutgoingBatchQueryService              batchQuery,
    IBatchStateMachine                      stateMachine,
    RetryProcessor                          retryProcessor,
    ICurrentUserService                     currentUser,
    IDistributedLockService                 lockService,
    IOptions<DistributedLockOptions>        lockOptions,
    IExportService<OutgoingBatchExportFilter> exporter,
    IExportAuditService                     exportAudit,
    OutgoingBatchExportFilterValidator      exportFilterValidator) : ControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<OutgoingBatchDto>), 200)]
    public async Task<IActionResult> GetBatches([FromQuery] BatchListRequest req, CancellationToken ct)
    {
        byte? status = null;
        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<BatchStatus>(req.Status, ignoreCase: true, out var parsed))
            status = (byte)parsed;

        var page = await batchQuery.GetBatchesAsync(new OutgoingBatchQueryFilter(
            req.NodeId, req.ChannelId, status, req.SortBy, req.SortDirection, req.Page, req.PageSize), ct);

        var totalPages = (int)Math.Ceiling(page.Total / (double)req.PageSize);
        var data = page.Items.Select(b => new OutgoingBatchDto(
            b.BatchId, (BatchStatus)b.Status, b.NodeId, b.ChannelId,
            b.CreateTime, b.SentTime, b.AckTime, b.RetryCount, b.RowCount, b.LatestError));

        return Ok(new PagedResponse<OutgoingBatchDto>(data, page.Total, req.Page, req.PageSize, totalPages));
    }

    [HttpGet("{batchId:long}")]
    [Authorize]
    [ProducesResponseType(typeof(OutgoingBatchDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetBatch(long batchId, CancellationToken ct)
    {
        var batch = await batchQuery.GetBatchByIdAsync(batchId, ct);
        if (batch is null) return NotFound();

        var dto = new OutgoingBatchDto(
            batch.BatchId, (BatchStatus)batch.Status, batch.NodeId, batch.ChannelId,
            batch.CreateTime, batch.SentTime, batch.AckTime, batch.RetryCount, batch.RowCount, batch.LatestError);

        return Ok(dto);
    }

    [HttpPost("{batchId:long}/retry")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(CodeMessageResponse), 409)]
    public async Task<IActionResult> RetryBatch(long batchId, CancellationToken ct)
    {
        var batch = await batchQuery.GetBatchByIdAsync(batchId, ct);
        if (batch is null) return NotFound();

        var transitioned = await stateMachine.MoveToRetryAsync(batchId, ct);

        if (!transitioned)
            return Conflict(new CodeMessageResponse(
                "INVALID_TRANSITION", $"Batch {batchId} is not in Error status"));

        return Ok();
    }

    [HttpPost("retry-all")]
    [Authorize(Policy = "OperatorOrAbove")]
    [ProducesResponseType(typeof(RetryAllResponse), 200)]
    [ProducesResponseType(typeof(CodeMessageResponse), 409)]
    public async Task<IActionResult> RetryAll(CancellationToken ct)
    {
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
        await using var handle = await lockService.TryAcquireAsync(
            LockNames.RetryEngine, owner, lockOptions.Value.DefaultExpiry, ct);

        if (handle == null)
            return Conflict(new CodeMessageResponse(
                "LOCK_UNAVAILABLE", "Retry engine is currently running. Try again shortly."));

        var count = await retryProcessor.ProcessAsync(ct);
        return Ok(new RetryAllResponse(count, DateTime.UtcNow, currentUser.GetCurrentUsername()));
    }

    [HttpGet("export")]
    [Authorize(Policy = "ViewerOrAbove")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> ExportBatches(
        [FromQuery] OutgoingBatchExportFilter filter,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        await exportFilterValidator.ValidateAndThrowAsync(filter, ct);

        var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return new MSOSync.Api.Results.StreamingExportResult(
            isJson
                ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
                : (s, t) => exporter.ExportCsvAsync(s, filter, t),
            isJson ? "application/json" : "text/csv",
            isJson ? $"batches-{date}.json" : $"batches-{date}.csv",
            (rows, ms) => exportAudit.WriteAsync("outgoing-batches", format, rows, ms, ct),
            ct);
    }
}
