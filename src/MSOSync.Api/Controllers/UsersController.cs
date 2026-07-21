using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Common;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.Users;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "AdminOnly")]
public sealed class UsersController(
    IUsersManagementService usersService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserSummaryDto>), 200)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20,
        [FromQuery] bool?   enabled  = null,
        [FromQuery] string? search   = null,
        CancellationToken ct = default)
    {
        var result = await usersService.GetUsersAsync(page, pageSize, enabled, search, ct);
        return Ok(result);
    }

    [HttpGet("{userId:long}")]
    [ProducesResponseType(typeof(UserDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetUser(long userId, CancellationToken ct)
    {
        var result = await usersService.GetUserAsync(userId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(UserDetailDto), 201)]
    [ProducesResponseType(typeof(ErrorResponse), 409)]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        UserDetailDto result;
        try
        {
            result = await usersService.CreateUserAsync(request, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already taken"))
        {
            return Conflict(new ErrorResponse(ex.Message));
        }

        return CreatedAtAction(nameof(GetUser), new { userId = result.UserId }, result);
    }

    [HttpPut("{userId:long}")]
    [ProducesResponseType(typeof(UserDetailDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> UpdateUser(
        long userId, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        var result = await usersService.UpdateUserAsync(userId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{userId:long}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> DeleteUser(long userId, CancellationToken ct)
    {
        var callerIdStr = User.FindFirstValue("userId");
        if (long.TryParse(callerIdStr, out var callerId) && callerId == userId)
            return Forbid();

        await usersService.DeactivateUserAsync(userId, ct);
        return NoContent();
    }
}
