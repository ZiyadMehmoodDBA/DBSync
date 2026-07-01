using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.IncomingBatches;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/incoming-batches")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class IncomingBatchesController(
    IIncomingBatchQueryService          batches,
    IValidator<IncomingBatchFilter>     validator,
    IExportService<IncomingBatchFilter> exporter,
    IExportAuditService                 exportAudit) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetIncomingBatches(
        [FromQuery] IncomingBatchFilter filter, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(filter, ct);
        return Ok(await batches.GetIncomingBatchesAsync(filter, ct));
    }

    [HttpGet("{batchId:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetIncomingBatchById(long batchId, CancellationToken ct)
    {
        var dto = await batches.GetIncomingBatchByIdAsync(batchId, ct);
        if (dto is null) throw new NotFoundException($"IncomingBatch {batchId} not found.");
        return Ok(dto);
    }

    [HttpGet("export")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> ExportIncomingBatches(
        [FromQuery] IncomingBatchFilter filter,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(filter, ct);
        var isJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return new MSOSync.Api.Results.StreamingExportResult(
            isJson
                ? (s, t) => exporter.ExportJsonAsync(s, filter, t)
                : (s, t) => exporter.ExportCsvAsync(s, filter, t),
            isJson ? "application/json" : "text/csv",
            isJson ? $"incoming-batches-{date}.json" : $"incoming-batches-{date}.csv",
            (rows, ms) => exportAudit.WriteAsync("incoming-batches", format, rows, ms));
    }
}
