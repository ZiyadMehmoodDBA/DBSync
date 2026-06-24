using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Users;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "AdminOnly")]
public sealed class UsersController(
    IUsersManagementService usersService) : ControllerBase
{
    [HttpGet]
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
    public async Task<IActionResult> GetUser(long userId, CancellationToken ct)
    {
        var result = await usersService.GetUserAsync(userId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
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
            return Conflict(new { error = ex.Message });
        }

        return CreatedAtAction(nameof(GetUser), new { userId = result.UserId }, result);
    }

    [HttpPut("{userId:long}")]
    public async Task<IActionResult> UpdateUser(
        long userId, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        try
        {
            var result = await usersService.UpdateUserAsync(userId, request, ct);
            return Ok(result);
        }
        catch (MSOSync.Common.Exceptions.NotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{userId:long}")]
    public async Task<IActionResult> DeleteUser(long userId, CancellationToken ct)
    {
        var callerIdStr = User.FindFirstValue("userId");
        if (long.TryParse(callerIdStr, out var callerId) && callerId == userId)
            return Forbid();

        try
        {
            await usersService.DeactivateUserAsync(userId, ct);
            return NoContent();
        }
        catch (MSOSync.Common.Exceptions.NotFoundException)
        {
            return NotFound();
        }
    }
}
