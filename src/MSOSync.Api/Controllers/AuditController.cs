using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Export;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class AuditController(
    IAuditQueryService          audit,
    IValidator<AuditFilter>     validator,
    IExportService<AuditFilter> exporter,
    IExportAuditService         exportAudit,
    IAuditSummaryService        summaryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetAudits(
        [FromQuery] AuditFilter filter, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(filter, ct);
        return Ok(await audit.GetAuditsAsync(filter, ct));
    }

    [HttpGet("{auditId:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetAuditById(long auditId, CancellationToken ct)
    {
        var dto = await audit.GetAuditByIdAsync(auditId, ct);
        if (dto is null) throw new NotFoundException($"Audit {auditId} not found.");
        return Ok(dto);
    }

    [HttpGet("summary")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetAuditSummary(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        if (from >= to)
            return BadRequest(new { code = "INVALID_RANGE", message = "'from' must be before 'to'" });
        return Ok(await summaryService.GetSummaryAsync(from, to, ct));
    }

    [HttpGet("export")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> ExportAudit(
        [FromQuery] AuditFilter filter,
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
            isJson ? $"audit-{date}.json" : $"audit-{date}.csv",
            (rows, ms) => exportAudit.WriteAsync("audit", format, rows, ms));
    }
}
