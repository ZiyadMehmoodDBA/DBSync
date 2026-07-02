// src/MSOSync.Api/Controllers/RolesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/roles")]
[Authorize(Policy = "AdminOnly")]
public sealed class RolesController(IPermissionService permissionService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RolePermissionsDto>), 200)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await permissionService.GetAllRolesAsync(ct));

    [HttpGet("{role}")]
    [ProducesResponseType(typeof(RolePermissionsDto), 200)]
    public async Task<IActionResult> GetRole(string role, CancellationToken ct)
        => Ok(await permissionService.GetRolePermissionsAsync(role, ct));

    [HttpPut("{role}/permissions/{key}")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Grant(string role, string key, CancellationToken ct)
    {
        await permissionService.GrantPermissionAsync(role, key, ct);
        return Ok();
    }

    [HttpDelete("{role}/permissions/{key}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Revoke(string role, string key, CancellationToken ct)
    {
        await permissionService.RevokePermissionAsync(role, key, ct);
        return NoContent();
    }

    [HttpPost("{role}/reset")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Reset(string role, CancellationToken ct)
    {
        await permissionService.ResetRoleToDefaultsAsync(role, ct);
        return Ok();
    }

    [HttpPost("{role}/copy-from/{sourceRole}")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> CopyFrom(string role, string sourceRole, CancellationToken ct)
    {
        await permissionService.CopyPermissionsFromAsync(role, sourceRole, ct);
        return Ok();
    }
}
