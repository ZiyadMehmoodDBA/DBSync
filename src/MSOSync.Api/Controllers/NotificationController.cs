// src/MSOSync.Api/Controllers/NotificationController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Common;
using MSOSync.Api.Dtos.Notifications;
using MSOSync.Common;
using MSOSync.Metadata.Notifications;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class NotificationController(
    INotificationQueryService queryService,
    ICurrentUserService       currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(NotificationPageDto), 200)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] string? cursor       = null,
        [FromQuery] int     pageSize     = 20,
        [FromQuery] bool    unreadOnly   = false,
        [FromQuery] string? severity     = null,
        CancellationToken   ct           = default)
    {
        var userId = await ResolveUserIdAsync(ct);
        var result = await queryService.GetPagedAsync(userId, cursor, Math.Clamp(pageSize, 1, 100), unreadOnly, severity, ct);
        return Ok(result);
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountResponse), 200)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        var count  = await queryService.GetUnreadCountAsync(userId, ct);
        return Ok(new UnreadCountResponse(count));
    }

    [HttpPost("{id:long}/read")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        await queryService.MarkReadAsync(userId, id, ct);
        return Ok();
    }

    [HttpPatch("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    public async Task<IActionResult> PatchNotification(
        long id, [FromBody] PatchNotificationRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new ErrorResponse("Request body is required."));
        if (!request.IsRead)
            return BadRequest(new ErrorResponse("Marking a notification as unread is not supported."));

        var userId = await ResolveUserIdAsync(ct);
        await queryService.MarkReadAsync(userId, id, ct);
        return Ok();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = await ResolveUserIdAsync(ct);
        await queryService.MarkAllReadAsync(userId, ct);
        return Ok();
    }

    private Task<long> ResolveUserIdAsync(CancellationToken ct)
        => queryService.ResolveUserIdAsync(currentUser.GetCurrentUsername(), ct);
}
