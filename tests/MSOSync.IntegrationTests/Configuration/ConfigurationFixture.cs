// tests/MSOSync.IntegrationTests/Configuration/ConfigurationFixture.cs
using System.Net.Http.Json;
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
using MSOSync.Api.Authorization;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.Configuration;

public sealed class ConfigurationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncConfiguration_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername    { get; } = "cfg-admin";
    public string AdminPassword    { get; } = "Admin123!";
    public string OperatorUsername { get; } = "cfg-operator";
    public string OperatorPassword { get; } = "Oper123!";

    public string NodeId    { get; } = "cfg-test-node";
    public string NodeToken { get; } = "cfg-test-node-token-12345abcde";

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
            ["RateLimit:LoginPermitLimit"]          = "1000",
            ["RateLimit:RefreshPermitLimit"]        = "1000",
            ["Heartbeat:IntervalSeconds"]           = "30",
            ["Heartbeat:MissedThreshold"]           = "3",
        });

        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddMetadata(testBuilder.Configuration);  // includes all config services
        testBuilder.Services.AddSingleton<IClock, SystemClock>();
        testBuilder.Services.AddTopologyServices();
        testBuilder.Services.AddHttpContextAccessor();
        testBuilder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        testBuilder.Services.AddScoped<INodeAuthorizationService, NodeAuthorizationService>();
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();
        testBuilder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<MSOSync.App.SignalR.NodeOperationsPublisher>());
        testBuilder.Services.AddSignalR();

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly)
            .AddJsonOptions(opts =>
                opts.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter()));

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
        app.MapHub<MSOSync.App.Hubs.OperationsHub>("/hubs/operations");
        app.MapGet("/health", () => Results.Ok(new { status = "UP" }));
        app.Start();
        return app;
    }

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        await using var db = new AppDbContext(opts);

        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncConfiguration_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        await GrantAsync(db, "ADMIN",    SystemPermissions.ManageUsers);
        await GrantAsync(db, "ADMIN",    SystemPermissions.ViewTopology);
        await GrantAsync(db, "ADMIN",    SystemPermissions.ManageNodeLifecycle);
        await GrantAsync(db, "ADMIN",    SystemPermissions.ManageConfigurations);
        await GrantAsync(db, "OPERATOR", SystemPermissions.ViewTopology);
        // OPERATOR intentionally lacks ManageConfigurations â€” used for 403 tests
        await db.SaveChangesAsync();

        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == "cfg-group"))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = "cfg-group", GroupName = "Config Test Group" });
        await db.SaveChangesAsync();

        if (!await db.Nodes.AnyAsync(n => n.NodeId == NodeId))
            db.Nodes.Add(new SyncNode
            {
                NodeId         = NodeId,
                GroupId        = "cfg-group",
                SyncUrl        = "http://cfg-node.test",
                LifecycleState = NodeLifecycleState.Active,
            });
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();
        if (!await db.NodeSecurities.AnyAsync(s => s.NodeId == NodeId))
            db.NodeSecurities.Add(new SyncNodeSecurity
            {
                NodeId           = NodeId,
                CurrentTokenHash = hasher.Hash(NodeToken),
                CreatedTime      = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        await CreateUserAsync(db, hasher, AdminUsername,    AdminPassword,    "ADMIN");
        await CreateUserAsync(db, hasher, OperatorUsername, OperatorPassword, "OPERATOR");
    }

    public new async Task DisposeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncConfiguration_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        catch { /* ignore teardown errors */ }
        await base.DisposeAsync();
    }

    public HttpClient NodeClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    NodeId);
        client.DefaultRequestHeaders.Add("X-Node-Token", NodeToken);
        return client;
    }

    public HttpClient NodeClientFor(string nodeId, string rawToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    nodeId);
        client.DefaultRequestHeaders.Add("X-Node-Token", rawToken);
        return client;
    }

    public async Task<(string NodeId, string RawToken)> CreateTestNodeAsync()
    {
        var nodeId   = "cfg-" + Guid.NewGuid().ToString("N")[..16];
        var rawToken = $"tok-{Guid.NewGuid():N}";
        var hasher   = new BCryptPasswordHasher();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Nodes.Add(new SyncNode
        {
            NodeId         = nodeId,
            GroupId        = "cfg-group",
            SyncUrl        = $"http://{nodeId}.test",
            LifecycleState = NodeLifecycleState.Active,
        });
        db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId           = nodeId,
            CurrentTokenHash = hasher.Hash(rawToken),
            CreatedTime      = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (nodeId, rawToken);
    }

    public async Task<string> GetJwtAsync(HttpClient client, string username, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { Username = username, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Login response was null");
        return body.Token;
    }

    private static async Task GrantAsync(AppDbContext db, string roleName, string permissionKey)
    {
        if (!await db.Permissions.AnyAsync(p => p.PermissionKey == permissionKey))
            db.Permissions.Add(new SyncPermission
            {
                PermissionKey = permissionKey,
                DisplayName   = permissionKey,
                Category      = "CONFIGURATION",
                SortOrder     = 99,
                IsSystem      = true,
            });
        await db.SaveChangesAsync();
        if (!await db.RolePermissions.AnyAsync(rp => rp.RoleName == roleName && rp.PermissionKey == permissionKey))
            db.RolePermissions.Add(new SyncRolePermission { RoleName = roleName, PermissionKey = permissionKey });
    }

    private static async Task CreateUserAsync(
        AppDbContext db, BCryptPasswordHasher hasher,
        string username, string password, string roleName)
    {
        if (!await db.Users.AnyAsync(u => u.Username == username))
        {
            var user = new SyncUser
            {
                Username     = username,
                PasswordHash = hasher.Hash(password),
                Enabled      = true,
                CreatedTime  = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var role = await db.Roles.FirstAsync(r => r.RoleName == roleName);
            db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = role.RoleId });
            await db.SaveChangesAsync();
        }
    }

    private sealed record LoginResponse(string Token, string RefreshToken);
}

[CollectionDefinition("Configuration")]
public sealed class ConfigurationCollection : ICollectionFixture<ConfigurationFixture> { }

