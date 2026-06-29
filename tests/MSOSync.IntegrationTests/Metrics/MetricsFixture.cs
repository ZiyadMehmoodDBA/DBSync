// tests/MSOSync.IntegrationTests/Metrics/MetricsFixture.cs
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

namespace MSOSync.IntegrationTests.Metrics;

public sealed class MetricsFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncMetrics_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername { get; } = "viewer-user";
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

        // Roles
        if (!await db.Roles.AnyAsync(r => r.RoleName == "VIEWER"))
            db.Roles.Add(new SyncRole { RoleName = "VIEWER" });
        if (!await db.Roles.AnyAsync(r => r.RoleName == "ADMIN"))
            db.Roles.Add(new SyncRole { RoleName = "ADMIN" });
        await db.SaveChangesAsync();

        // Viewer user
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

        await SeedAsync(db);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Nodes.AnyAsync()) return;

        // Nodes
        db.Nodes.AddRange(
            new SyncNode { NodeId = "hub-1",   GroupId = "group-hub",   SyncUrl = "http://hub-1",   Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Reachable  },
            new SyncNode { NodeId = "store-1", GroupId = "group-store", SyncUrl = "http://store-1", Status = "REGISTERED", ConnectivityStatus = ConnectivityStatus.Degraded   });
        await db.SaveChangesAsync();

        // DataEvents (2 pending on ch-default, 1 processed on ch-config)
        db.DataEvents.AddRange(
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "hub-1", ChannelId = "ch-default", EventType = 'I', TableName = "dbo.Order", CreateTime = DateTime.UtcNow.AddHours(-1), IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "hub-1", ChannelId = "ch-default", EventType = 'U', TableName = "dbo.Order", CreateTime = DateTime.UtcNow.AddHours(-2), IsProcessed = false },
            new SyncDataEvent { TriggerId = "t2", SourceNodeId = "hub-1", ChannelId = "ch-config",  EventType = 'I', TableName = "dbo.Config", CreateTime = DateTime.UtcNow.AddHours(-1), IsProcessed = true  });
        await db.SaveChangesAsync();

        // IncomingBatches
        db.IncomingBatches.AddRange(
            new SyncIncomingBatch { BatchId = 2001L, NodeId = "hub-1",   SourceNodeId = "hub-1",   ChannelId = "ch-default", BatchSequence = 1L, Status = IncomingBatchStatus.Applied, ReceivedTime = DateTime.UtcNow.AddHours(-3), AppliedTime = DateTime.UtcNow.AddHours(-3).AddMilliseconds(80),  ApplyTimeMs = 80L  },
            new SyncIncomingBatch { BatchId = 2002L, NodeId = "store-1", SourceNodeId = "hub-1",   ChannelId = "ch-default", BatchSequence = 2L, Status = IncomingBatchStatus.Applied, ReceivedTime = DateTime.UtcNow.AddHours(-2), AppliedTime = DateTime.UtcNow.AddHours(-2).AddMilliseconds(120), ApplyTimeMs = 120L },
            new SyncIncomingBatch { BatchId = 2003L, NodeId = "hub-1",   SourceNodeId = "hub-1",   ChannelId = "ch-config",  BatchSequence = 3L, Status = IncomingBatchStatus.New,     ReceivedTime = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // OutgoingBatches (one pending, one acknowledged)
        var ob1 = new SyncOutgoingBatch { BatchSequence = 1L, NodeId = "hub-1",   ChannelId = "ch-default", Status = 0 }; // New = pending
        var ob2 = new SyncOutgoingBatch { BatchSequence = 2L, NodeId = "store-1", ChannelId = "ch-default", Status = 2 }; // Acknowledged
        db.OutgoingBatches.AddRange(ob1, ob2);
        await db.SaveChangesAsync();

        // BatchError linked to ob2 (already acknowledged, but error within 24h)
        db.BatchErrors.Add(new SyncBatchError { BatchId = ob2.BatchId, ErrorMessage = "conflict", CreateTime = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();

        // RuntimeStats (2 snapshots — no NodeId on this entity)
        db.RuntimeStats.AddRange(
            new SyncRuntimeStats { HeapUsed = 512_000_000L, HeapMax = 1_024_000_000L, ThreadCount = 20, CpuPercent = 12.5m, GcCount = 100L, GcTimeMs = 200L, UptimeMs = 3_600_000L, CreateTime = DateTime.UtcNow.AddMinutes(-10) },
            new SyncRuntimeStats { HeapUsed = 600_000_000L, HeapMax = 1_024_000_000L, ThreadCount = 22, CpuPercent = 18.0m, GcCount = 110L, GcTimeMs = 220L, UptimeMs = 3_660_000L, CreateTime = DateTime.UtcNow.AddMinutes(-5)  });
        await db.SaveChangesAsync();

        // SyncMonitor rows
        db.Monitors.AddRange(
            new SyncMonitor { NodeId = "hub-1",   MetricName = "cpu",    MetricValue = "12.5", CreateTime = DateTime.UtcNow.AddMinutes(-5)  },
            new SyncMonitor { NodeId = "hub-1",   MetricName = "memory", MetricValue = "512",  CreateTime = DateTime.UtcNow.AddMinutes(-5)  },
            new SyncMonitor { NodeId = "store-1", MetricName = "cpu",    MetricValue = "5.0",  CreateTime = DateTime.UtcNow.AddMinutes(-10) });
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
            "ALTER DATABASE [MSOSyncMetrics_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Metrics")]
public sealed class MetricsCollection : ICollectionFixture<MetricsFixture> { }
