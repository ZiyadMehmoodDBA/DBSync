// tests/MSOSync.IntegrationTests/Permissions/PermissionsIntegrationTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
using MSOSync.Metadata.Permissions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.Permissions;

// ── Fixture ───────────────────────────────────────────────────────────────────

public sealed class PermissionsFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncPermissions_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername  { get; } = "perm-viewer";
    public string ViewerPassword  { get; } = "ViewP@ss1!";
    public string OperatorUsername { get; } = "perm-operator";
    public string OperatorPassword { get; } = "OpP@ss1!";
    public string AdminUsername   { get; } = "perm-admin";
    public string AdminPassword   { get; } = "AdminP@ss1!";

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
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        await using var db = new AppDbContext(opts);

        // Drop and recreate for a clean slate on every run
        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncPermissions_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        // MigrateAsync also runs M018_Permissions which seeds the permissions catalog
        // and default role-permission rows for VIEWER/OPERATOR/ADMIN
        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();

        await CreateUserAsync(db, hasher, ViewerUsername,   ViewerPassword,   "VIEWER");
        await CreateUserAsync(db, hasher, OperatorUsername, OperatorPassword, "OPERATOR");
        await CreateUserAsync(db, hasher, AdminUsername,    AdminPassword,    "ADMIN");
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
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncPermissions_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public async Task<HttpClient> ViewerClientAsync()  => await MakeClientAsync(ViewerUsername,   ViewerPassword);
    public async Task<HttpClient> OperatorClientAsync() => await MakeClientAsync(OperatorUsername, OperatorPassword);
    public async Task<HttpClient> AdminClientAsync()   => await MakeClientAsync(AdminUsername,    AdminPassword);

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
}

[CollectionDefinition("Permissions")]
public sealed class PermissionsCollection : ICollectionFixture<PermissionsFixture> { }

// ── Tests ─────────────────────────────────────────────────────────────────────

[Collection("Permissions")]
public sealed class PermissionsIntegrationTests(PermissionsFixture fx)
{
    // ── GET /api/v1/me/permissions ───────────────────────────────────────────

    [Fact]
    public async Task GetMyPermissions_AsViewer_Returns200WithViewPermissionsOnly()
    {
        var client   = await fx.ViewerClientAsync();
        var response = await client.GetFromJsonAsync<EffectivePermissionsDto>("/api/v1/me/permissions");

        response!.Role.Should().Be("VIEWER");
        response.Permissions.Should().Contain("VIEW_EVENTS");
        response.Permissions.Should().NotContain("RETRY_BATCHES");
    }

    [Fact]
    public async Task GetMyPermissions_AsOperator_IncludesRetryBatches()
    {
        var client   = await fx.OperatorClientAsync();
        var response = await client.GetFromJsonAsync<EffectivePermissionsDto>("/api/v1/me/permissions");

        response!.Permissions.Should().Contain("RETRY_BATCHES");
    }

    [Fact]
    public async Task GetMyPermissions_AsAdmin_ReturnsAllFifteen()
    {
        var client   = await fx.AdminClientAsync();
        var response = await client.GetFromJsonAsync<EffectivePermissionsDto>("/api/v1/me/permissions");

        response!.Permissions.Should().HaveCount(15);
    }

    // ── GET /api/v1/permissions (catalog) ────────────────────────────────────

    [Fact]
    public async Task GetCatalog_AsViewer_Returns200WithAllFifteenPermissions()
    {
        var client   = await fx.ViewerClientAsync();
        var response = await client.GetFromJsonAsync<PermissionDto[]>("/api/v1/permissions");

        response!.Should().HaveCount(15);
        response.Should().Contain(p => p.PermissionKey == "VIEW_EVENTS");
        response.Should().Contain(p => p.PermissionKey == "MANAGE_USERS");
    }

    // ── GET /api/v1/roles ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRoles_AsAdmin_Returns200WithThreeRoles()
    {
        var client   = await fx.AdminClientAsync();
        var response = await client.GetFromJsonAsync<RolePermissionsDto[]>("/api/v1/roles");

        response!.Should().HaveCount(3);
        response.Should().Contain(r => r.RoleName == "ADMIN");
        response.Should().Contain(r => r.RoleName == "VIEWER");
        response.Should().Contain(r => r.RoleName == "OPERATOR");
    }

    [Fact]
    public async Task GetRoles_AsViewer_Returns403()
    {
        var client   = await fx.ViewerClientAsync();
        var response = await client.GetAsync("/api/v1/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/v1/roles/{role} ─────────────────────────────────────────────

    [Fact]
    public async Task GetRole_AsAdmin_Returns200WithViewerPermissions()
    {
        var client   = await fx.AdminClientAsync();
        var response = await client.GetFromJsonAsync<RolePermissionsDto>("/api/v1/roles/VIEWER");

        response!.RoleName.Should().Be("VIEWER");
        response.Permissions.Should().Contain(p => p.PermissionKey == "VIEW_EVENTS");
        response.Permissions.Should().NotContain(p => p.PermissionKey == "MANAGE_USERS");
    }

    // ── PUT /api/v1/roles/{role}/permissions/{key} ───────────────────────────

    [Fact]
    public async Task GrantPermission_AsAdmin_Returns200AndPermissionTakesEffect()
    {
        var admin   = await fx.AdminClientAsync();

        // Grant RETRY_BATCHES to VIEWER
        var putResp = await admin.PutAsync("/api/v1/roles/VIEWER/permissions/RETRY_BATCHES", null);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify via GET me/permissions for viewer
        var viewer = await fx.ViewerClientAsync();
        var perms  = await viewer.GetFromJsonAsync<EffectivePermissionsDto>("/api/v1/me/permissions");
        perms!.Permissions.Should().Contain("RETRY_BATCHES");

        // Cleanup: reset VIEWER to defaults so other tests are not affected
        await admin.PostAsync("/api/v1/roles/VIEWER/reset", null);
    }

    [Fact]
    public async Task GrantPermission_AsViewer_Returns403()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.PutAsync("/api/v1/roles/VIEWER/permissions/RETRY_BATCHES", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DELETE /api/v1/roles/{role}/permissions/{key} ────────────────────────

    [Fact]
    public async Task RevokeManageUsersFromAdmin_Returns400PermissionProtected()
    {
        var admin    = await fx.AdminClientAsync();
        var response = await admin.DeleteAsync("/api/v1/roles/ADMIN/permissions/MANAGE_USERS");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/v1/roles/{role}/reset ──────────────────────────────────────

    [Fact]
    public async Task ResetRole_AsAdmin_Returns200AndRestoresDefaults()
    {
        var admin = await fx.AdminClientAsync();

        // First grant an extra permission to OPERATOR
        await admin.PutAsync("/api/v1/roles/OPERATOR/permissions/MANAGE_USERS", null);

        // Reset
        var resetResp = await admin.PostAsync("/api/v1/roles/OPERATOR/reset", null);
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify reset — MANAGE_USERS should be gone from OPERATOR
        var role = await admin.GetFromJsonAsync<RolePermissionsDto>("/api/v1/roles/OPERATOR");
        role!.Permissions.Should().NotContain(p => p.PermissionKey == "MANAGE_USERS");
    }

    // ── POST /api/v1/roles/{role}/copy-from/{sourceRole} ─────────────────────

    [Fact]
    public async Task CopyFrom_AsAdmin_Returns200AndViewerHasOperatorPermissions()
    {
        var admin = await fx.AdminClientAsync();

        // Reset OPERATOR to defaults so we have a stable expected count (11 permissions)
        await admin.PostAsync("/api/v1/roles/OPERATOR/reset", null);

        // Copy OPERATOR permissions → VIEWER
        var resp = await admin.PostAsync("/api/v1/roles/VIEWER/copy-from/OPERATOR", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var viewer = await fx.ViewerClientAsync();
        var perms  = await viewer.GetFromJsonAsync<EffectivePermissionsDto>("/api/v1/me/permissions");
        // OPERATOR defaults: 12 permissions (11 from M018 + MANAGE_NODE_LIFECYCLE from M022)
        perms!.Permissions.Should().HaveCount(12);

        // Cleanup: reset VIEWER to defaults so other tests are not affected
        await admin.PostAsync("/api/v1/roles/VIEWER/reset", null);
    }

    // ── Audit entry ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditEntryWritten_AfterGrant()
    {
        var admin = await fx.AdminClientAsync();
        await admin.PutAsync("/api/v1/roles/VIEWER/permissions/EXPORT_DATA", null);

        // Verify via audit endpoint — uses the real /api/v1/audit paged response
        var auditResp = await admin.GetAsync("/api/v1/audit?pageSize=50");
        auditResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var body  = await auditResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");

        // Find an audit row for GRANT_PERMISSION on VIEWER/EXPORT_DATA
        var grantEntry = Enumerable.Range(0, items.GetArrayLength())
            .Select(i => items[i])
            .FirstOrDefault(e =>
                e.GetProperty("actionName").GetString() == "GRANT_PERMISSION" &&
                e.GetProperty("objectName").GetString()!.Contains("VIEWER") &&
                e.GetProperty("objectName").GetString()!.Contains("EXPORT_DATA"));

        grantEntry.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "an audit row for GRANT_PERMISSION on roles/VIEWER|EXPORT_DATA should exist");
        grantEntry.GetProperty("username").GetString().Should().NotBeNullOrEmpty(
            "the acting admin username must be recorded in the audit trail");

        // Cleanup
        await admin.PostAsync("/api/v1/roles/VIEWER/reset", null);
    }
}
