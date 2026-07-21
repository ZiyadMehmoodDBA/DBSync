using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Audit;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Export;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class AuditController(
    IAuditQueryService            audit,
    IValidator<AuditFilter>       validator,
    IValidator<AuditSummaryRequest> summaryValidator,
    IExportService<AuditFilter>   exporter,
    IExportAuditService           exportAudit,
    IAuditSummaryService          summaryService,
    CorrelationTimelineAssembler  assembler) : ControllerBase
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
        [FromQuery] AuditSummaryRequest request,
        CancellationToken ct)
    {
        await summaryValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await summaryService.GetSummaryAsync(request.From, request.To, ct));
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
            (rows, ms) => exportAudit.WriteAsync("audit", format, rows, ms, ct));
    }

    // GET /api/v1/audit/correlation/{correlationId}
    [HttpGet("correlation/{correlationId}")]
    [ProducesResponseType<CorrelationTimelineDto>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetCorrelationAsync(
        string correlationId, CancellationToken ct)
    {
        var result = await assembler.AssembleAsync(correlationId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // GET /api/v1/audit/correlations/search?q=...&from=...&to=...
    [HttpGet("correlations/search")]
    [ProducesResponseType<CorrelationSearchResultDto[]>(200)]
    public async Task<IActionResult> SearchCorrelationsAsync(
        [FromQuery] string? q,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var results = await assembler.SearchAsync(q, from, to, ct);
        return Ok(results);
    }

    // GET /api/v1/audit/correlation/{correlationId}/export?format=markdown
    [HttpGet("correlation/{correlationId}/export")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(501)]
    public async Task<IActionResult> ExportCorrelationAsync(
        string correlationId,
        [FromQuery] string format = "json",
        CancellationToken ct = default)
    {
        var timeline = await assembler.AssembleAsync(correlationId, ct);
        if (timeline is null) return NotFound();

        return format.ToLowerInvariant() switch
        {
            "json"     => Ok(timeline),
            "markdown" => Content(BuildMarkdown(timeline), "text/markdown"),
            "pdf"      => StatusCode(501, new { message = "PDF export is not implemented in 12C. Planned for a future release." }),
            _          => BadRequest(new { message = $"Unsupported export format '{format}'. Supported: json, markdown." })
        };
    }

    private static string BuildMarkdown(CorrelationTimelineDto t)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Correlation Timeline: {t.CorrelationId}");
        sb.AppendLine();
        sb.AppendLine($"- **Started:** {t.StartedAt:O}");
        sb.AppendLine($"- **Completed:** {t.CompletedAt:O}");
        sb.AppendLine($"- **Duration:** {t.Duration}");
        sb.AppendLine($"- **Operation Type:** {t.OperationType ?? "N/A"}");
        sb.AppendLine($"- **Status:** {t.OperationStatus ?? "N/A"}");
        sb.AppendLine($"- **Initiated By:** {t.InitiatedBy ?? "System"}");
        if (t.IsFailedWorkflow)
        {
            sb.AppendLine();
            sb.AppendLine($"> **Failed Workflow:** {t.FailureSummary}");
        }
        sb.AppendLine();
        if (t.EntityChips.Length > 0)
        {
            sb.AppendLine("## Entities");
            foreach (var chip in t.EntityChips)
                sb.AppendLine($"- {chip.EntityType}: {chip.EntityId}{(chip.DisplayLabel is not null ? $" ({chip.DisplayLabel})" : "")}");
            sb.AppendLine();
        }
        foreach (var phase in t.Phases)
        {
            sb.AppendLine($"## Phase: {phase.PhaseName}");
            sb.AppendLine();
            sb.AppendLine("| Time | Action | Actor | Severity | Summary |");
            sb.AppendLine("|------|--------|-------|----------|---------|");
            foreach (var e in phase.Events)
                sb.AppendLine($"| {e.OccurredAt:HH:mm:ss.fff} | {e.ActionName} | {e.ActorUsername ?? "system"} | {e.Severity} | {e.Summary} |");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
