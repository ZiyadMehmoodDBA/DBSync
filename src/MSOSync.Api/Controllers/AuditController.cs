using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class AuditController(
    IAuditQueryService          audit,
    IValidator<AuditFilter>     validator) : ControllerBase
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
}
