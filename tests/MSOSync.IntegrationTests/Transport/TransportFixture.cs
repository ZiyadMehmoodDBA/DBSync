using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Scheduler;
using MSOSync.Security;
using MSOSync.Topology;
using MSOSync.Transport;
using MSOSync.Trigger;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Transport;

public sealed class TransportFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";
    public const string LocalNodeId  = "test-node";
    public const string SourceNodeId = "source-node";
    public const string GroupId      = "test";
    public const string TestToken    = "test-token";

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
            ["Node:Id"]              = LocalNodeId,
            ["Node:GroupId"]         = GroupId,
            ["Node:SyncUrl"]         = "http://localhost",
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
        testBuilder.Services.AddSyncScheduler(testBuilder.Configuration);
        testBuilder.Services.Configure<NodeProperties>(testBuilder.Configuration.GetSection("Node"));
        testBuilder.Services.AddTransportServices(testBuilder.Configuration);
        testBuilder.Services.AddTopologyServices();

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        var app = testBuilder.Build();
        app.UseExceptionHandler();
        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseNodeTokenAuth();
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

        // Node group (FK required by SyncNode)
        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == GroupId))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = GroupId, GroupName = "Test Group" });
        await db.SaveChangesAsync();

        // Local node (the node this app pretends to be)
        if (!await db.Nodes.AnyAsync(n => n.NodeId == LocalNodeId))
            db.Nodes.Add(new SyncNode
            {
                NodeId        = LocalNodeId,
                GroupId       = GroupId,
                SyncUrl       = "http://localhost",
                Status        = "APPROVED",
                SyncEnabled   = true,
                TransportMode = TransportMode.Pull,
            });
        await db.SaveChangesAsync();

        // Source node (the node that will push / authenticate)
        if (!await db.Nodes.AnyAsync(n => n.NodeId == SourceNodeId))
            db.Nodes.Add(new SyncNode
            {
                NodeId        = SourceNodeId,
                GroupId       = GroupId,
                SyncUrl       = "http://source",
                Status        = "APPROVED",
                SyncEnabled   = true,
                TransportMode = TransportMode.Pull,
            });
        await db.SaveChangesAsync();

        // NodeSecurity for source-node so NodeTokenAuthMiddleware can validate "test-token"
        // Low work factor (4) for test speed
        if (!await db.NodeSecurities.AnyAsync(s => s.NodeId == SourceNodeId))
            db.NodeSecurities.Add(new SyncNodeSecurity
            {
                NodeId           = SourceNodeId,
                CurrentTokenHash = BCrypt.Net.BCrypt.HashPassword(TestToken, workFactor: 4),
                CreatedTime      = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        // Default channel (FK for SyncIncomingBatch / SyncOutgoingBatch)
        if (!await db.Channels.AnyAsync(c => c.ChannelId == "default"))
            db.Channels.Add(new SyncChannel
            {
                ChannelId      = "default",
                Priority       = 1,
                BatchSize      = 1000,
                MaxBatchToSend = 10,
                MaxDataSize    = 1048576L,
            });
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.StopAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Transport")]
public sealed class TransportCollection : ICollectionFixture<TransportFixture> { }
