using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Security;
using MSOSync.Persistence;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/security")]
[Authorize(Policy = "AdminOnly")]
public sealed class SecurityAuditController(AppDbContext db, IAuditChainService chainService) : ControllerBase
{
    [HttpGet("audit")]
    public async Task<ActionResult<object>> GetAudit(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var total = await db.Audits.CountAsync(ct);
        var entries = await db.Audits
            .AsNoTracking()
            .OrderByDescending(e => e.CreateTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.AuditId,
                e.Username,
                e.ActionName,
                e.ObjectName,
                e.CorrelationId,
                e.CreateTime,
                e.EntryHash,
            })
            .ToListAsync(ct);
        return Ok(new { total, page, page_size = pageSize, items = entries });
    }

    [HttpGet("audit/verify")]
    public async Task<ActionResult<object>> VerifyChain(CancellationToken ct = default)
    {
        var (isValid, brokenId) = await chainService.VerifyChainAsync(ct);
        return Ok(new { is_valid = isValid, first_broken_id = brokenId });
    }
}
