// tests/MSOSync.IntegrationTests/System/SystemFixture.cs
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
using MSOSync.App.Export;
using MSOSync.App.Health;
using MSOSync.App.Workers;
using MSOSync.Common;
using MSOSync.Common.Health;
using MSOSync.Common.Workers;
using MSOSync.Metadata;
using MSOSync.Metadata.Configuration;
using MSOSync.Metadata.Export;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[CollectionDefinition("SystemAdmin")]
public sealed class SystemAdminCollection : ICollectionFixture<SystemFixture> { }

public sealed class SystemFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncSystem_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername  { get; } = "sys-admin";
    public string AdminPassword  { get; } = "AdminP@ss1!";
    public string ViewerUsername { get; } = "sys-viewer";
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
            ["RateLimit:LoginPermitLimit"]          = "1000",
            ["RateLimit:RefreshPermitLimit"]        = "1000",
            ["Heartbeat:IntervalSeconds"]           = "30",
            ["Heartbeat:MissedThreshold"]           = "3",
            ["Export:ImmediateThreshold"]           = "50000",
            ["Export:BasePath"]                     = "exports-test",
            ["Export:RetentionHours"]               = "24",
            ["Export:MaxConcurrentJobs"]            = "1",
        });

        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddMetadata(testBuilder.Configuration);
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

        // Make IKeyedServiceProvider injectable (needed by OperationService)
        testBuilder.Services.AddSingleton<IKeyedServiceProvider>(
            sp => (IKeyedServiceProvider)sp);

        // Epic 12C: Worker status registry + system health
        testBuilder.Services.AddSingleton<IWorkerStatusRegistry, WorkerStatusRegistry>();
        testBuilder.Services.AddSingleton<ISystemHealthService, SystemHealthService>();
        testBuilder.Services.AddSingleton<ISystemHealthContributor, WorkerHealthContributor>();
        testBuilder.Services.AddSingleton<ISystemHealthContributor, DatabaseHealthContributor>();
        testBuilder.Services.AddSingleton<ISystemHealthContributor, ApiHealthContributor>();
        testBuilder.Services.AddSingleton<ISystemHealthContributor, SignalRHealthContributor>();

        testBuilder.Services.Configure<ExportOptions>(
            testBuilder.Configuration.GetSection("Export"));
        testBuilder.Services.AddScoped<IExportJobService, ExportJobService>();

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly)
            .AddJsonOptions(opts =>
                opts.JsonSerializerOptions.Converters.Add(
                    new global::System.Text.Json.Serialization.JsonStringEnumConverter()));

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
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr)
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new AppDbContext(opts);

        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncSystem_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        // Use EnsureCreated to build the schema from the current EF model.
        // This avoids migration designer-file dependencies and PendingModelChangesWarning.
        await db.Database.EnsureCreatedAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        await GrantAsync(db, "ADMIN",  MSOSync.Metadata.Permissions.SystemPermissions.ManageUsers);
        await GrantAsync(db, "ADMIN",  MSOSync.Metadata.Permissions.SystemPermissions.ViewTopology);
        await GrantAsync(db, "ADMIN",  MSOSync.Metadata.Permissions.SystemPermissions.ManageNodeLifecycle);
        await GrantAsync(db, "ADMIN",  MSOSync.Metadata.Permissions.SystemPermissions.ManageConfigurations);
        await GrantAsync(db, "VIEWER", MSOSync.Metadata.Permissions.SystemPermissions.ViewTopology);

        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == "sys-group"))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = "sys-group", GroupName = "System Test Group" });
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();
        await CreateUserAsync(db, hasher, AdminUsername,  AdminPassword,  "ADMIN");
        await CreateUserAsync(db, hasher, ViewerUsername, ViewerPassword, "VIEWER");

        // Seed a FeatureFlag parameter for administration tests
        if (!await db.Parameters.AnyAsync(p => p.ParameterName == "SYS_TEST_FLAG"))
        {
            db.Parameters.Add(new SyncParameter
            {
                ParameterName  = "SYS_TEST_FLAG",
                ParameterValue = "true",
                Category       = "FeatureFlag",
                DisplayName    = "System Test Flag",
                Description    = "Used only by integration tests",
                ValueType      = "Boolean",
                DisplayOrder   = 999,
            });
            await db.SaveChangesAsync();
        }
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr)
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new AppDbContext(opts);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncSystem_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        catch { /* ignore teardown errors */ }
        await base.DisposeAsync();
    }

    public async Task<HttpClient> AdminClientAsync() =>
        await MakeClientAsync(AdminUsername, AdminPassword);

    public async Task<HttpClient> ViewerClientAsync() =>
        await MakeClientAsync(ViewerUsername, ViewerPassword);

    private async Task<HttpClient> MakeClientAsync(string username, string password)
    {
        var loginClient = CreateClient();
        var resp = await loginClient.PostAsJsonAsync("/api/v1/auth/login",
            new { username, password });
        resp.EnsureSuccessStatusCode();
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Seeds N sync_operation rows for performance tests.</summary>
    public async Task SeedOperationsAsync(int count)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var statuses = new[] { "Pending", "Running", "Completed", "Failed", "Cancelled" };
        var types    = new[] { "Export",  "Rollout", "Decommission", "Recovery" };
        var rng      = new global::System.Random(42);

        var operations = Enumerable.Range(0, count).Select(i => new SyncOperation
        {
            OperationId   = global::System.Guid.NewGuid(),
            OperationType = types[i % types.Length],
            Status        = statuses[i % statuses.Length],
            Source        = "System",
            StartedAt     = global::System.DateTime.UtcNow.AddMinutes(-rng.Next(1, 10000)),
            CorrelationId = global::System.Guid.NewGuid().ToString(),
        }).ToList();

        await db.Operations.AddRangeAsync(operations);
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds N sync_node rows for performance tests.</summary>
    public async Task SeedNodesAsync(int count)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (int i = 0; i < count; i++)
        {
            var nodeId = $"perf-node-{i:D5}";
            if (!await db.Nodes.AnyAsync(n => n.NodeId == nodeId))
            {
                db.Nodes.Add(new SyncNode
                {
                    NodeId         = nodeId,
                    GroupId        = "sys-group",
                    SyncUrl        = $"http://perf-node-{i}.test",
                    LifecycleState = NodeLifecycleState.Active,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task GrantAsync(AppDbContext db, string roleName, string permissionKey)
    {
        if (!await db.Permissions.AnyAsync(p => p.PermissionKey == permissionKey))
            db.Permissions.Add(new SyncPermission
            {
                PermissionKey = permissionKey,
                DisplayName   = permissionKey,
                Category      = "SYSTEM",
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
        if (await db.Users.AnyAsync(u => u.Username == username)) return;

        var user = new SyncUser
        {
            Username     = username,
            PasswordHash = hasher.Hash(password),
            Enabled      = true,
            CreatedTime  = global::System.DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.FirstAsync(r => r.RoleName == roleName);
        db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = role.RoleId });
        await db.SaveChangesAsync();
    }
}
