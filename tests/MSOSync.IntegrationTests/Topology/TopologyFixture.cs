using System.Net.Http.Json;
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
using Xunit;

namespace MSOSync.IntegrationTests.Topology;

public sealed class TopologyFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncTopology_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername { get; } = "topology-viewer";
    public string ViewerPassword { get; } = "TopologyP@ss1!";

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

        // Seed roles
        if (!await db.Roles.AnyAsync(r => r.RoleName == "VIEWER"))
            db.Roles.Add(new SyncRole { RoleName = "VIEWER" });
        if (!await db.Roles.AnyAsync(r => r.RoleName == "ADMIN"))
            db.Roles.Add(new SyncRole { RoleName = "ADMIN" });
        await db.SaveChangesAsync();

        // Seed viewer user
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

            var viewerRole = await db.Roles.FirstAsync(r => r.RoleName == "VIEWER");
            db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = viewerRole.RoleId });
            await db.SaveChangesAsync();
        }

        await SeedTopologyDataAsync(db);
    }

    private static async Task SeedTopologyDataAsync(AppDbContext db)
    {
        if (await db.NodeGroups.AnyAsync()) return;

        // 3 groups: hub (2 nodes), store (1 node), empty (0 nodes)
        db.NodeGroups.AddRange(
            new SyncNodeGroup { GroupId = "group-hub",   GroupName = "Hub"         },
            new SyncNodeGroup { GroupId = "group-store", GroupName = "Store"       },
            new SyncNodeGroup { GroupId = "group-empty", GroupName = "Empty Group" });
        await db.SaveChangesAsync();

        // Nodes: hub has 1 Reachable + 1 Degraded; store has 1 Reachable
        db.Nodes.AddRange(
            new SyncNode { NodeId = "hub-1",   GroupId = "group-hub",   SyncUrl = "http://hub-1",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable },
            new SyncNode { NodeId = "hub-2",   GroupId = "group-hub",   SyncUrl = "http://hub-2",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Degraded  },
            new SyncNode { NodeId = "store-1", GroupId = "group-store", SyncUrl = "http://store-1", Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable });
        await db.SaveChangesAsync();

        // Router: hub → store
        db.Routers.Add(new SyncRouter
        {
            RouterId        = "router-hub-store",
            SourceNodeGroup = "group-hub",
            TargetNodeGroup = "group-store",
            Enabled         = true
        });
        await db.SaveChangesAsync();

        // Triggers with 2 distinct channels
        db.Triggers.AddRange(
            new SyncTrigger { TriggerId = "trig-1", SourceTable = "dbo.Product", ChannelId = "ch-default" },
            new SyncTrigger { TriggerId = "trig-2", SourceTable = "dbo.Config",  ChannelId = "ch-config"  });
        await db.SaveChangesAsync();

        // TriggerRouters linking both triggers to the router
        db.TriggerRouters.AddRange(
            new SyncTriggerRouter { TriggerId = "trig-1", RouterId = "router-hub-store" },
            new SyncTriggerRouter { TriggerId = "trig-2", RouterId = "router-hub-store" });
        await db.SaveChangesAsync();
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

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncTopology_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Topology")]
public sealed class TopologyCollection : ICollectionFixture<TopologyFixture> { }
