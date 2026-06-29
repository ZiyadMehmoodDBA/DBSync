using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using System.Net.Http.Json;
using Xunit;

namespace MSOSync.IntegrationTests.Audit;

public sealed class AuditFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncAudit_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername { get; } = "audit-viewer";
    public string ViewerPassword { get; } = "ViewP@ss1!";
    public string AdminUsername  { get; } = "audit-admin";
    public string AdminPassword  { get; } = "AdminP@ss1!";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testBuilder = WebApplication.CreateBuilder();
        testBuilder.WebHost.UseTestServer();

        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnStr,
            ["Jwt:Secret"]                          = JwtSecret,
            ["Jwt:Issuer"]                          = "msosync",
            ["Jwt:Audience"]                        = "msosync-dashboard",
            ["Jwt:AccessExpiryMinutes"]             = "60",
            ["RateLimit:LoginPermitLimit"]          = "100",
            ["RateLimit:RefreshPermitLimit"]        = "100",
        });

        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddMetadata(testBuilder.Configuration);
        testBuilder.Services.AddSingleton<IClock, SystemClock>();
        testBuilder.Services.AddTopologyServices();
        testBuilder.Services.AddHttpContextAccessor();
        testBuilder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        testBuilder.Services.AddFluentValidationAutoValidation();
        testBuilder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

        var app = testBuilder.Build();

        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseNodeTokenAuth();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "UP" }));

        app.Start();
        return app;
    }

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);

        // Drop and recreate for a clean slate on every run
        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncAudit_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();

        if (!await db.Users.AnyAsync(u => u.Username == ViewerUsername))
        {
            var user = new SyncUser
            {
                Username     = ViewerUsername,
                PasswordHash = hasher.Hash(ViewerPassword),
                Enabled      = true,
                CreatedTime  = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var role = await db.Roles.FirstAsync(r => r.RoleName == "VIEWER");
            db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = role.RoleId });
            await db.SaveChangesAsync();
        }

        if (!await db.Users.AnyAsync(u => u.Username == AdminUsername))
        {
            var user = new SyncUser
            {
                Username     = AdminUsername,
                PasswordHash = hasher.Hash(AdminPassword),
                Enabled      = true,
                CreatedTime  = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var role = await db.Roles.FirstAsync(r => r.RoleName == "ADMIN");
            db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = role.RoleId });
            await db.SaveChangesAsync();
        }

        await SeedAsync(db);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Audits.AnyAsync()) return;

        db.Audits.AddRange(
            new SyncAudit { Username = "alice", ActionName = "UPDATE", ObjectName = "SyncNode",    CorrelationId = "corr-1", CreateTime = DateTime.UtcNow.AddMinutes(-30) },
            new SyncAudit { Username = "bob",   ActionName = "DELETE", ObjectName = "SyncTrigger", CorrelationId = null,     CreateTime = DateTime.UtcNow.AddMinutes(-20) },
            new SyncAudit { Username = "alice", ActionName = "CREATE", ObjectName = "SyncRouter",  CorrelationId = "corr-3", CreateTime = DateTime.UtcNow.AddMinutes(-10) });
        await db.SaveChangesAsync();

        db.Locks.AddRange(
            new SyncLock { LockName = "RETRY_ENGINE", LockOwner = "worker-1", LockTime = DateTime.UtcNow.AddMinutes(-5) },
            new SyncLock { LockName = "SYNC_ENGINE",  LockOwner = "worker-2", LockTime = DateTime.UtcNow.AddMinutes(-2) });
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncAudit_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public async Task<string> GetViewerTokenAsync()
    {
        var client = CreateClient();
        var resp   = await client.PostAsJsonAsync("api/v1/auth/login", new
        {
            username = ViewerUsername,
            password = ViewerPassword
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    public async Task<string> GetAdminTokenAsync()
    {
        var client = CreateClient();
        var resp   = await client.PostAsJsonAsync("api/v1/auth/login", new
        {
            username = AdminUsername,
            password = AdminPassword
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }
}

[CollectionDefinition("Audit")]
public sealed class AuditCollectionDefinition : ICollectionFixture<AuditFixture> { }
