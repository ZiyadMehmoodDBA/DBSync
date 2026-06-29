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

namespace MSOSync.IntegrationTests.Dashboard;

public sealed class DashboardFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncDashboard_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername { get; } = "dash-viewer";
    public string ViewerPassword { get; } = "ViewP@ss1!";

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
        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        if (!await db.Users.AnyAsync(u => u.Username == ViewerUsername))
        {
            var hasher = new BCryptPasswordHasher();
            var user   = new SyncUser
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

        await SeedAsync(db);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Nodes.AnyAsync()) return;

        db.Nodes.AddRange(
            new SyncNode { NodeId = "hub-1",   GroupId = "g1", SyncUrl = "http://hub-1",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable  },
            new SyncNode { NodeId = "hub-2",   GroupId = "g1", SyncUrl = "http://hub-2",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable  },
            new SyncNode { NodeId = "store-1", GroupId = "g2", SyncUrl = "http://store-1", Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Degraded   });
        await db.SaveChangesAsync();

        db.DataEvents.AddRange(
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "hub-1", ChannelId = "ch1", EventType = 'I', TableName = "T", CreateTime = DateTime.UtcNow.AddHours(-1), IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "hub-1", ChannelId = "ch1", EventType = 'U', TableName = "T", CreateTime = DateTime.UtcNow.AddHours(-2), IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "hub-1", ChannelId = "ch1", EventType = 'D', TableName = "T", CreateTime = DateTime.UtcNow.AddHours(-3), IsProcessed = true  });
        await db.SaveChangesAsync();

        var pending = new SyncOutgoingBatch { BatchSequence = 1L, NodeId = "hub-1",   ChannelId = "ch1", Status = 0 };
        var acked   = new SyncOutgoingBatch { BatchSequence = 2L, NodeId = "store-1", ChannelId = "ch1", Status = 2 };
        db.OutgoingBatches.AddRange(pending, acked);
        await db.SaveChangesAsync();

        db.BatchErrors.Add(new SyncBatchError { BatchId = acked.BatchId, ErrorMessage = "conflict", CreateTime = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();

        db.Audits.AddRange(
            new SyncAudit { AuditId = 1, Username = "alice", ActionName = "UPDATE", ObjectName = "SyncNode",    CreateTime = DateTime.UtcNow.AddMinutes(-30) },
            new SyncAudit { AuditId = 2, Username = "bob",   ActionName = "DELETE", ObjectName = "SyncTrigger", CreateTime = DateTime.UtcNow.AddMinutes(-20) },
            new SyncAudit { AuditId = 3, Username = "alice", ActionName = "CREATE", ObjectName = "SyncRouter",  CreateTime = DateTime.UtcNow.AddMinutes(-10) });
        await db.SaveChangesAsync();
    }

    public new Task DisposeAsync() => Task.CompletedTask;

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
}

[CollectionDefinition("Dashboard")]
public sealed class DashboardCollectionDefinition : ICollectionFixture<DashboardFixture> { }
