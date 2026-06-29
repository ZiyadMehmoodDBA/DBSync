using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Locks;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/locks")]
public sealed class LocksController(ILockAdminService locks) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "ViewerOrAbove")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetLocks(CancellationToken ct)
    {
        return Ok(await locks.GetLocksAsync(ct));
    }

    [HttpDelete("{lockName}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> DeleteLock(string lockName, CancellationToken ct)
    {
        var deleted = await locks.DeleteLockAsync(lockName, ct);
        if (!deleted) throw new NotFoundException($"Lock '{lockName}' not found.");
        return NoContent();
    }
}
