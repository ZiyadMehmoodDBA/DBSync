// tests/MSOSync.IntegrationTests/Export/ExportJobIntegrationTests.cs
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
using MSOSync.Api.Controllers;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.App.Export;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Metadata.Export;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.Export;

// ── Fixture ───────────────────────────────────────────────────────────────────

public sealed class ExportJobFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncExportJobs_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername  { get; } = "export-viewer";
    public string ViewerPassword  { get; } = "ViewP@ss1!";
    public string Viewer2Username { get; } = "export-viewer2";
    public string Viewer2Password { get; } = "ViewP@ss2!";
    public string AdminUsername   { get; } = "export-admin";
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
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();

        testBuilder.Services.Configure<ExportOptions>(
            testBuilder.Configuration.GetSection("Export"));
        testBuilder.Services.AddScoped<IExportJobService, ExportJobService>();

        testBuilder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<MSOSync.App.SignalR.NodeOperationsPublisher>());

        testBuilder.Services.AddSignalR();

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
        app.MapHub<MSOSync.App.Hubs.OperationsHub>("/hubs/operations");
        app.MapGet("/health", () => Results.Ok(new { status = "UP" }));

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
                "ALTER DATABASE [MSOSyncExportJobs_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        // Migration seeds EXPORT_DATA for OPERATOR/ADMIN and MANAGE_USERS for ADMIN.
        // VIEWER needs EXPORT_DATA too so these export tests can create/manage jobs.
        await GrantIfMissingAsync(db, "VIEWER", "EXPORT_DATA");
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();
        await CreateUserAsync(db, hasher, ViewerUsername,  ViewerPassword,  "VIEWER");
        await CreateUserAsync(db, hasher, Viewer2Username, Viewer2Password, "VIEWER");
        await CreateUserAsync(db, hasher, AdminUsername,   AdminPassword,   "ADMIN");
    }

    private static async Task GrantIfMissingAsync(AppDbContext db, string roleName, string permissionKey)
    {
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
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncExportJobs_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public async Task<HttpClient> ViewerClientAsync()  => await MakeClientAsync(ViewerUsername,  ViewerPassword);
    public async Task<HttpClient> Viewer2ClientAsync() => await MakeClientAsync(Viewer2Username, Viewer2Password);
    public async Task<HttpClient> AdminClientAsync()   => await MakeClientAsync(AdminUsername,   AdminPassword);

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

[CollectionDefinition("ExportJobs")]
public sealed class ExportJobCollection : ICollectionFixture<ExportJobFixture> { }

// ── Tests ─────────────────────────────────────────────────────────────────────

[Collection("ExportJobs")]
public sealed class ExportJobIntegrationTests(ExportJobFixture fx)
{
    [Fact]
    public async Task CreateJob_AsViewer_Returns202WithJobId()
    {
        var client  = await fx.ViewerClientAsync();
        var request = new CreateExportJobRequest("events", "csv", "{}", null);
        var resp    = await client.PostAsJsonAsync("/api/v1/export-jobs", request);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetJobs_ReturnsOnlyCallerJobs()
    {
        var viewer1 = await fx.ViewerClientAsync();
        var viewer2 = await fx.Viewer2ClientAsync();

        // viewer1 creates a job
        await viewer1.PostAsJsonAsync("/api/v1/export-jobs",
            new CreateExportJobRequest("events", "csv", "{}", null));

        // viewer2 should see zero jobs (their own only)
        var jobs = await viewer2.GetFromJsonAsync<ExportJobDto[]>("/api/v1/export-jobs");
        jobs.Should().NotBeNull();
        jobs!.Should().NotContain(j => j.RequestedBy == fx.ViewerUsername);
    }

    [Fact]
    public async Task GetJobsAllTrue_AsViewer_Returns403()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.GetAsync("/api/v1/export-jobs?all=true");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetJobsAllTrue_AsAdmin_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/export-jobs?all=true");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DownloadJob_AsOtherViewer_Returns403()
    {
        var owner = await fx.ViewerClientAsync();
        var other = await fx.Viewer2ClientAsync();

        var createResp = await owner.PostAsJsonAsync("/api/v1/export-jobs",
            new CreateExportJobRequest("events", "csv", "{}", null));
        var body  = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = body.GetProperty("jobId").GetString()!;

        var resp = await other.GetAsync($"/api/v1/export-jobs/{jobId}/download");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteJob_AsOwner_Returns204AndMakesJobNotFound()
    {
        var client = await fx.ViewerClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/export-jobs",
            new CreateExportJobRequest("events", "csv", "{}", null));
        var body  = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = body.GetProperty("jobId").GetString()!;

        var deleteResp = await client.DeleteAsync($"/api/v1/export-jobs/{jobId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Soft-deleted: download returns 404
        var download = await client.GetAsync($"/api/v1/export-jobs/{jobId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
