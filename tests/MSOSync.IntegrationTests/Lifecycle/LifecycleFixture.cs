using System.Net.Http.Headers;
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
using MSOSync.Api.Authorization;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

public sealed class LifecycleFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncLifecycle_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername    { get; } = "lc-viewer";
    public string ViewerPassword    { get; } = "ViewP@ss1!";
    public string ApproverUsername  { get; } = "lc-approver";
    public string ApproverPassword  { get; } = "ApprP@ss1!";
    public string OperatorUsername  { get; } = "lc-operator";
    public string OperatorPassword  { get; } = "OprP@ss1!";
    public string AdminUsername     { get; } = "lc-admin";
    public string AdminPassword     { get; } = "AdminP@ss1!";

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
            ["Heartbeat:IntervalSeconds"]           = "30",
            ["Heartbeat:ProbeIntervalSeconds"]      = "60",
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

        testBuilder.Services.AddScoped<INodeAuthorizationService, NodeAuthorizationService>();

        testBuilder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<MSOSync.App.SignalR.NodeOperationsPublisher>());

        testBuilder.Services.AddSignalR();
        testBuilder.Services.AddHttpClient(); // required by NodeDecommissionNotifier

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
        app.MapGet("/health", () => Results.Ok(new { status = "UP" }));
        app.MapHub<MSOSync.App.Hubs.OperationsHub>("/hubs/operations");

        app.Start();
        return app;
    }

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);

        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncLifecycle_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        // Grant permissions per role
        await GrantIfMissingAsync(db, "VIEWER",   SystemPermissions.ViewTopology);
        await GrantIfMissingAsync(db, "OPERATOR", SystemPermissions.ViewTopology);
        await GrantIfMissingAsync(db, "OPERATOR", SystemPermissions.ApproveNodes);
        await GrantIfMissingAsync(db, "OPERATOR", SystemPermissions.ManageNodeLifecycle);
        await GrantIfMissingAsync(db, "ADMIN",    SystemPermissions.ViewTopology);
        await GrantIfMissingAsync(db, "ADMIN",    SystemPermissions.ApproveNodes);
        await GrantIfMissingAsync(db, "ADMIN",    SystemPermissions.ManageUsers);
        await GrantIfMissingAsync(db, "ADMIN",    SystemPermissions.ProvisionNodes);
        await GrantIfMissingAsync(db, "ADMIN",    SystemPermissions.ManageNodeLifecycle);
        await db.SaveChangesAsync();

        // Seed a default node group
        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == "test-group"))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = "test-group", GroupName = "Test Group" });
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();
        await CreateUserAsync(db, hasher, ViewerUsername,   ViewerPassword,   "VIEWER");
        await CreateUserAsync(db, hasher, ApproverUsername, ApproverPassword, "OPERATOR");
        await CreateUserAsync(db, hasher, OperatorUsername, OperatorPassword, "OPERATOR");
        await CreateUserAsync(db, hasher, AdminUsername,    AdminPassword,    "ADMIN");
    }

    private static async Task GrantIfMissingAsync(AppDbContext db, string roleName, string permissionKey)
    {
        // Ensure permission row exists
        if (!await db.Permissions.AnyAsync(p => p.PermissionKey == permissionKey))
            db.Permissions.Add(new SyncPermission
            {
                PermissionKey = permissionKey,
                DisplayName   = permissionKey,
                Category      = "OPERATIONS",
                SortOrder     = 99,
                IsSystem      = true,
            });

        var exists = await db.RolePermissions.AnyAsync(
            rp => rp.RoleName == roleName && rp.PermissionKey == permissionKey);
        if (!exists)
            db.RolePermissions.Add(new SyncRolePermission
            {
                RoleName      = roleName,
                PermissionKey = permissionKey,
            });
    }

    private static async Task CreateUserAsync(
        AppDbContext db, BCryptPasswordHasher hasher,
        string username, string password, string roleName)
    {
        if (await db.Users.AnyAsync(u => u.Username == username)) return;
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

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncLifecycle_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        catch { /* ignore if not exists */ }
        await base.DisposeAsync();
    }

    // ── Client helpers ─────────────────────────────────────────────────────────

    public async Task<HttpClient> ViewerClientAsync()   => await MakeClientAsync(ViewerUsername,   ViewerPassword);
    public async Task<HttpClient> ApproverClientAsync() => await MakeClientAsync(ApproverUsername, ApproverPassword);
    public async Task<HttpClient> LifecycleManagerClientAsync() => await MakeClientAsync(OperatorUsername, OperatorPassword);
    public async Task<HttpClient> AdminClientAsync()    => await MakeClientAsync(AdminUsername,    AdminPassword);
    public HttpClient AnonymousClient()                 => CreateClient();

    private async Task<HttpClient> MakeClientAsync(string username, string password)
    {
        var loginClient = CreateClient();
        var resp = await loginClient.PostAsJsonAsync("api/v1/auth/login",
            new { username, password });
        resp.EnsureSuccessStatusCode();
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Lifecycle test helpers ─────────────────────────────────────────────────

    /// <summary>Creates a SyncNode in the given lifecycle state, returns NodeId.</summary>
    public async Task<string> SeedNodeAsync(
        NodeLifecycleState state, string externalId, Action<SyncNode>? mutate = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Remove any existing node with this externalId to ensure test isolation
        var existing = await db.Nodes.FirstOrDefaultAsync(n => n.ExternalId == externalId);
        if (existing is not null)
        {
            // Remove associated history, tokens, security
            await db.NodeLifecycleHistories.Where(h => h.NodeId == existing.NodeId).ExecuteDeleteAsync();
            await db.NodeBootstrapTokens.Where(t => t.NodeId == existing.NodeId).ExecuteDeleteAsync();
            var sec = await db.NodeSecurities.FirstOrDefaultAsync(s => s.NodeId == existing.NodeId);
            if (sec is not null) db.NodeSecurities.Remove(sec);
            db.Nodes.Remove(existing);
            await db.SaveChangesAsync();
        }

        var nodeId = externalId; // NodeId = ExternalId for test nodes (matches registration pattern)
        var node = new SyncNode
        {
            NodeId         = nodeId,
            GroupId        = "test-group",
            SyncUrl        = $"https://{externalId}.local:8080",
            LifecycleState = state,
            ExternalId     = externalId,
            NodeName       = externalId,
            NodeType       = "source",
        };
        mutate?.Invoke(node);
        db.Nodes.Add(node);

        // Seed a history row (M022 pattern)
        db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId    = nodeId,
            FromState = null,
            ToState   = state,
            Trigger   = LifecycleTrigger.Migration,
            Reason    = "Test seed",
            Actor     = "test",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
        return nodeId;
    }

    /// <summary>Issues a one-time bootstrap token and returns the raw token.</summary>
    public async Task<string> IssueBootstrapTokenAsync(string nodeId)
    {
        await using var scope = Services.CreateAsyncScope();
        var bootstrapSvc = scope.ServiceProvider.GetRequiredService<IBootstrapTokenService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var raw = await bootstrapSvc.IssueAsync(nodeId, "test", CancellationToken.None);
        await db.SaveChangesAsync();
        return raw;
    }

    /// <summary>Issues an operational node token (for heartbeat auth) and returns the raw token.</summary>
    public async Task<string> IssueNodeTokenAsync(string nodeId)
    {
        await using var scope = Services.CreateAsyncScope();
        var nodeSecurity = scope.ServiceProvider.GetRequiredService<NodeSecurityService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = nodeSecurity.PrepareToken(nodeId);
        await db.SaveChangesAsync();
        return result.RawToken;
    }

    /// <summary>Creates an HTTP client with the node-token auth headers set.</summary>
    public HttpClient NodeClient(string nodeId, string nodeToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    nodeId);
        client.DefaultRequestHeaders.Add("X-Node-Token", nodeToken);
        return client;
    }

    /// <summary>Revokes all active bootstrap tokens for a node.</summary>
    public async Task RevokeBootstrapTokensAsync(string nodeId)
    {
        await using var scope = Services.CreateAsyncScope();
        var bootstrapSvc = scope.ServiceProvider.GetRequiredService<IBootstrapTokenService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await bootstrapSvc.RevokeAllAsync(nodeId, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    /// <summary>Calls GET api/v1/node-lifecycle/nodes/{nodeId}/state via the lifecycle manager client.</summary>
    public async Task<JsonElement> GetNodeStateViaApiAsync(string nodeId)
    {
        var client = await LifecycleManagerClientAsync();
        var resp = await client.GetAsync($"api/v1/node-lifecycle/nodes/{nodeId}/state");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }
}

[CollectionDefinition("Lifecycle")]
public sealed class LifecycleCollection : ICollectionFixture<LifecycleFixture> { }
