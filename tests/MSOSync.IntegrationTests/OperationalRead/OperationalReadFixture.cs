// tests/MSOSync.IntegrationTests/OperationalRead/OperationalReadFixture.cs
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

namespace MSOSync.IntegrationTests.OperationalRead;

public sealed class OperationalReadFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncOperationalRead_Test;" +
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

        // Seed source node (required for FK on IncomingBatch via SourceNodeId)
        // SyncNode requires GroupId (varchar, no DB FK to sync_node_group)
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "test-node-1"))
        {
            db.Nodes.Add(new SyncNode
            {
                NodeId   = "test-node-1",
                GroupId  = "test-group-1",
                SyncUrl  = "http://test-node-1",
                Status   = "REGISTERED",
            });
            await db.SaveChangesAsync();
        }

        // Seed test data
        await SeedTestDataAsync(db);
    }

    private static async Task SeedTestDataAsync(AppDbContext db)
    {
        // Seed Events (5 total: 3 processed, 2 not)
        if (!await db.DataEvents.AnyAsync())
        {
            var events = new[]
            {
                new SyncDataEvent { TriggerId = "trig-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'I', TableName = "dbo.Product", CreateTime = DateTime.UtcNow.AddHours(-2), IsProcessed = true  },
                new SyncDataEvent { TriggerId = "trig-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'U', TableName = "dbo.Product", CreateTime = DateTime.UtcNow.AddHours(-1), IsProcessed = true  },
                new SyncDataEvent { TriggerId = "trig-2", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'D', TableName = "dbo.Order",   CreateTime = DateTime.UtcNow,               IsProcessed = true  },
                new SyncDataEvent { TriggerId = "trig-2", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'I', TableName = "dbo.Order",   CreateTime = DateTime.UtcNow.AddMinutes(-5), IsProcessed = false },
                new SyncDataEvent { TriggerId = "trig-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", EventType = 'U', TableName = "dbo.Product", CreateTime = DateTime.UtcNow.AddMinutes(-1), IsProcessed = false },
            };
            db.DataEvents.AddRange(events);
            await db.SaveChangesAsync();
        }

        // Seed IncomingBatches (3: 1 Applied, 1 Error, 1 New)
        // BatchId is ValueGeneratedNever so explicit IDs are fine
        if (!await db.IncomingBatches.AnyAsync())
        {
            db.IncomingBatches.AddRange(
                new SyncIncomingBatch { BatchId = 1001L, NodeId = "test-node-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", Status = IncomingBatchStatus.Applied, BatchSequence = 1L, ReceivedTime = DateTime.UtcNow.AddHours(-2), AppliedTime = DateTime.UtcNow.AddHours(-2).AddMilliseconds(120), ApplyTimeMs = 120L },
                new SyncIncomingBatch { BatchId = 1002L, NodeId = "test-node-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", Status = IncomingBatchStatus.Error,   BatchSequence = 2L, ReceivedTime = DateTime.UtcNow.AddHours(-1) },
                new SyncIncomingBatch { BatchId = 1003L, NodeId = "test-node-1", SourceNodeId = "test-node-1", ChannelId = "ch-1", Status = IncomingBatchStatus.New,     BatchSequence = 3L, ReceivedTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Seed OutgoingBatches (2 rows) — needed as FK parent for SyncBatchError.BatchId
        // BatchId is identity (ValueGeneratedOnAdd); we query back the generated IDs.
        long[] outgoingBatchIds;
        if (!await db.OutgoingBatches.AnyAsync())
        {
            var ob1 = new SyncOutgoingBatch { BatchSequence = 1L, NodeId = "test-node-1", ChannelId = "ch-1", Status = 2 };
            var ob2 = new SyncOutgoingBatch { BatchSequence = 2L, NodeId = "test-node-1", ChannelId = "ch-1", Status = 3 };
            db.OutgoingBatches.AddRange(ob1, ob2);
            await db.SaveChangesAsync();
            outgoingBatchIds = new[] { ob1.BatchId, ob2.BatchId };
        }
        else
        {
            outgoingBatchIds = await db.OutgoingBatches
                .OrderBy(b => b.BatchSequence)
                .Select(b => b.BatchId)
                .Take(2)
                .ToArrayAsync();
        }

        // Seed BatchErrors (4: Info + 2 Warning + Critical; 2 from today, 2 from yesterday)
        // ErrorId is ValueGeneratedOnAdd (identity). BatchId must reference OutgoingBatch.
        if (!await db.BatchErrors.AnyAsync())
        {
            long bid1 = outgoingBatchIds[0];
            long bid2 = outgoingBatchIds.Length > 1 ? outgoingBatchIds[1] : outgoingBatchIds[0];

            db.BatchErrors.AddRange(
                new SyncBatchError { BatchId = bid1, ConflictType = "DuplicateKey",   ErrorMessage = "Duplicate",        RetryCount = 0, CreateTime = DateTime.UtcNow.AddDays(-1) },
                new SyncBatchError { BatchId = bid2, ConflictType = "Timeout",        ErrorMessage = "Timeout occurred", RetryCount = 2, CreateTime = DateTime.UtcNow.AddDays(-1) },
                new SyncBatchError { BatchId = bid2, ConflictType = "Deadlock",       ErrorMessage = "Deadlock",         RetryCount = 1, CreateTime = DateTime.UtcNow              },
                new SyncBatchError { BatchId = bid2, ConflictType = "MetadataMissing",ErrorMessage = "Missing meta",     RetryCount = 0, CreateTime = DateTime.UtcNow              });
            await db.SaveChangesAsync();
        }
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
        // LoginResponse uses "token" (case-insensitive JSON deserialization)
        return body.GetProperty("token").GetString()!;
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncOperationalRead_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

// Registers the fixture for sharing across all [Collection("OperationalRead")] test classes.
// ICollectionFixture<T> (not IClassFixture<T>) — one instance shared, not one per class.
[CollectionDefinition("OperationalRead")]
public sealed class OperationalReadCollection : ICollectionFixture<OperationalReadFixture> { }
