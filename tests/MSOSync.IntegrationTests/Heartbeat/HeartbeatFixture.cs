// tests/MSOSync.IntegrationTests/Heartbeat/HeartbeatFixture.cs
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

namespace MSOSync.IntegrationTests.Heartbeat;

public sealed class HeartbeatFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncHeartbeat_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    public string NodeId    { get; } = "hb-node";
    public string NodeToken { get; } = "test-node-token-value-12345";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testBuilder = WebApplication.CreateBuilder();
        testBuilder.WebHost.UseTestServer();

        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnStr,
            ["Jwt:Secret"]                          = "test-jwt-secret-value-at-least-32-chars!",
            ["Jwt:Issuer"]                          = "msosync",
            ["Jwt:Audience"]                        = "msosync-dashboard",
            ["Jwt:AccessExpiryMinutes"]             = "60",
            ["RateLimit:LoginPermitLimit"]          = "100",
            ["RateLimit:RefreshPermitLimit"]        = "100",
            ["Node:NodeId"]                         = NodeId,
            ["Node:SyncUrl"]                        = "http://localhost",
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

        if (!await db.Roles.AnyAsync(r => r.RoleName == "ADMIN"))
            db.Roles.Add(new SyncRole { RoleName = "ADMIN" });
        await db.SaveChangesAsync();

        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == "hb-group"))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = "hb-group", GroupName = "HB Test" });
        await db.SaveChangesAsync();

        if (!await db.Nodes.AnyAsync(n => n.NodeId == NodeId))
        {
            db.Nodes.Add(new SyncNode
            {
                NodeId   = NodeId,
                GroupId  = "hb-group",
                SyncUrl  = "http://localhost",
                Status   = "REGISTERED",
            });
        }
        await db.SaveChangesAsync();

        // Seed node security with BCrypt hash of the known raw token
        if (!await db.NodeSecurities.AnyAsync(s => s.NodeId == NodeId))
        {
            var hasher = new BCryptPasswordHasher();
            db.NodeSecurities.Add(new SyncNodeSecurity
            {
                NodeId           = NodeId,
                CurrentTokenHash = hasher.Hash(NodeToken),
                CreatedTime      = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncHeartbeat_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Heartbeat")]
public sealed class HeartbeatCollection : ICollectionFixture<HeartbeatFixture> { }
