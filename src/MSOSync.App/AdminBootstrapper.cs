using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Workers;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.App;

public sealed class AdminBootstrapper(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminBootstrapper> logger,
    IWorkerStatusRegistry registry) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        registry.Register(nameof(AdminBootstrapper), TimeSpan.FromDays(365)); // one-shot; interval is irrelevant
        registry.RecordTickStart(nameof(AdminBootstrapper), TickTrigger.Startup);

        try
        {
            var adminUser = configuration["Admin:Username"]
                ?? Environment.GetEnvironmentVariable("MSOSYNC_ADMIN_USER");
            var adminPassword = configuration["Admin:Password"]
                ?? Environment.GetEnvironmentVariable("MSOSYNC_ADMIN_PASSWORD");

            if (string.IsNullOrEmpty(adminUser) || string.IsNullOrEmpty(adminPassword))
            {
                logger.LogDebug("MSOSYNC_ADMIN_USER / MSOSYNC_ADMIN_PASSWORD not set — skipping admin bootstrap");
                registry.RecordTickComplete(nameof(AdminBootstrapper));
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<BCryptPasswordHasher>();

            if (await db.Users.AnyAsync(u => u.Username == adminUser, ct))
            {
                logger.LogDebug("Admin user '{Username}' already exists — skipping bootstrap", adminUser);
                registry.RecordTickComplete(nameof(AdminBootstrapper));
                return;
            }

            var adminRole = await db.Roles
                .FirstOrDefaultAsync(r => r.RoleName == "ADMIN", ct);

            if (adminRole == null)
            {
                logger.LogWarning("ADMIN role not found in database — run migrations first");
                registry.RecordTickComplete(nameof(AdminBootstrapper));
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
            registry.RecordTickComplete(nameof(AdminBootstrapper));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AdminBootstrapper failed during startup — admin user may not be available");
            registry.RecordTickFailed(nameof(AdminBootstrapper), ex);
            // Do not rethrow: a missing admin user is not a fatal startup condition
            // (the user can be created manually or via re-run after the DB is available)
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
