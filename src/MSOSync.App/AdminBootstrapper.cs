using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.App;

public sealed class AdminBootstrapper(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var adminUser = configuration["Admin:Username"]
            ?? Environment.GetEnvironmentVariable("MSOSYNC_ADMIN_USER");
        var adminPassword = configuration["Admin:Password"]
            ?? Environment.GetEnvironmentVariable("MSOSYNC_ADMIN_PASSWORD");

        if (string.IsNullOrEmpty(adminUser) || string.IsNullOrEmpty(adminPassword))
        {
            logger.LogDebug("MSOSYNC_ADMIN_USER / MSOSYNC_ADMIN_PASSWORD not set — skipping admin bootstrap");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<BCryptPasswordHasher>();

        if (await db.Users.AnyAsync(ct))
        {
            logger.LogDebug("Users already exist — skipping admin bootstrap");
            return;
        }

        var adminRole = await db.Roles
            .FirstOrDefaultAsync(r => r.RoleName == "ADMIN", ct);

        if (adminRole == null)
        {
            logger.LogWarning("ADMIN role not found in database — run migrations first");
            return;
        }

        var user = new SyncUser
        {
            Username = adminUser,
            PasswordHash = hasher.Hash(adminPassword),
            Enabled = true,
            CreatedTime = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = adminRole.RoleId });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Admin user '{Username}' created", adminUser);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
