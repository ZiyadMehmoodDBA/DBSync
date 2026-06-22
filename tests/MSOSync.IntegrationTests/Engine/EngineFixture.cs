using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Batch;
using MSOSync.Metadata;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Security;
using MSOSync.Trigger;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Engine;

public sealed class EngineFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";
    public const string NodeId    = "hub";
    public const string ChannelId = "default";
    public const string TriggerId = "t-engine-1";
    public const string GroupId   = "default";
    public const string RouterId  = "r-engine-1";
    public const string TestTable = "msosync.sync_test_source";

    private string? _connStr;

    public AppDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connStr!)
            .Options;
        return new AppDbContext(opts);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testBuilder = WebApplication.CreateBuilder();
        testBuilder.WebHost.UseTestServer();

        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connStr,
            ["Jwt:Secret"]           = JwtSecret,
            ["Node:Id"]              = NodeId,
            ["Sync:IntervalSeconds"] = "30",
        });

        testBuilder.Environment.EnvironmentName = "Test";

        testBuilder.Services.AddEndpointsApiExplorer();
        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddHttpContextAccessor();
        testBuilder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();
        testBuilder.Services.AddMetadata(testBuilder.Configuration);
        testBuilder.Services.AddSingleton<IClock, SystemClock>();
        testBuilder.Services.AddTriggerEngine(testBuilder.Configuration);
        testBuilder.Services.AddEventServices();
        testBuilder.Services.AddRoutingServices();
        testBuilder.Services.AddBatchPipeline(testBuilder.Configuration);
        testBuilder.Services.AddSyncEngine(testBuilder.Configuration);

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        var app = testBuilder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Start();

        return app;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connStr = _container.GetConnectionString();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        // Roles
        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        // Node group
        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == GroupId))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = GroupId, GroupName = "Default Group" });
        await db.SaveChangesAsync();

        // Node
        if (!await db.Nodes.AnyAsync(n => n.NodeId == NodeId))
            db.Nodes.Add(new SyncNode
            {
                NodeId          = NodeId,
                GroupId         = GroupId,
                SyncUrl         = "http://hub:8080",
                Status          = "REGISTERED",
                LastHeartbeat   = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        // Channel
        if (!await db.Channels.AnyAsync(c => c.ChannelId == ChannelId))
            db.Channels.Add(new SyncChannel
            {
                ChannelId    = ChannelId,
                Priority     = 1,
                BatchSize    = 1000,
                MaxBatchToSend = 10,
                MaxDataSize  = 1048576L,
            });
        await db.SaveChangesAsync();

        // Router — routes events from the "default" group to the "default" group (hub node)
        if (!await db.Routers.AnyAsync(r => r.RouterId == RouterId))
            db.Routers.Add(new SyncRouter
            {
                RouterId        = RouterId,
                SourceNodeGroup = GroupId,
                TargetNodeGroup = GroupId,
            });
        await db.SaveChangesAsync();

        // Trigger
        if (!await db.Triggers.AnyAsync(t => t.TriggerId == TriggerId))
            db.Triggers.Add(new SyncTrigger
            {
                TriggerId   = TriggerId,
                ChannelId   = ChannelId,
                SourceTable = TestTable,
                Enabled     = true,
            });
        await db.SaveChangesAsync();

        // TriggerRouter
        if (!await db.TriggerRouters.AnyAsync(tr => tr.TriggerId == TriggerId && tr.RouterId == RouterId))
            db.TriggerRouters.Add(new SyncTriggerRouter { TriggerId = TriggerId, RouterId = RouterId });
        await db.SaveChangesAsync();

        // Create test source table
        await db.Database.ExecuteSqlRawAsync($"""
            IF OBJECT_ID(N'{TestTable}', N'U') IS NULL
            CREATE TABLE {TestTable} (
                id   INT IDENTITY(1,1) PRIMARY KEY,
                name NVARCHAR(100) NOT NULL
            )
            """);
    }

    public new async Task DisposeAsync()
    {
        await _container.StopAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Engine")]
public sealed class EngineCollection : ICollectionFixture<EngineFixture> { }
