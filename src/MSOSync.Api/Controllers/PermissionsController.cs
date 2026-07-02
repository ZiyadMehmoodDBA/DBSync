// src/MSOSync.Api/Controllers/PermissionsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class PermissionsController(
    IPermissionService permissionService,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("me/permissions")]
    [ProducesResponseType(typeof(EffectivePermissionsDto), 200)]
    public async Task<IActionResult> GetMyPermissions(CancellationToken ct)
        => Ok(await permissionService.GetEffectivePermissionsAsync(currentUser.GetCurrentUsername(), ct));

    [HttpGet("permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), 200)]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
        => Ok(await permissionService.GetAllPermissionsAsync(ct));
}
