using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.App.Hubs;

[Authorize(Policy = "ViewerOrAbove")]
public sealed class OperationsHub(IServiceScopeFactory scopeFactory) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "operators");
        await AddToUserGroupAsync();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "operators");
        await base.OnDisconnectedAsync(exception);
    }

    private async Task AddToUserGroupAsync()
    {
        var username = Context.User?.Identity?.Name;
        if (username is null) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => (long?)u.UserId)
            .FirstOrDefaultAsync();

        if (userId.HasValue)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
    }
}
