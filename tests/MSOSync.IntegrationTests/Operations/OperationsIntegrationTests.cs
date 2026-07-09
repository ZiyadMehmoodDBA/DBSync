using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Metadata.Operations;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;
using FluentValidation;
using FluentValidation.AspNetCore;
using MSOSync.Api.Controllers;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App.Export;
using MSOSync.Metadata.Export;

namespace MSOSync.IntegrationTests.Operations;

// ── Fixture ───────────────────────────────────────────────────────────────────

public sealed class OperationsFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncOperations_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername { get; } = "ops-admin";
    public string AdminPassword { get; } = "AdminP@ss1!";
    public string ViewerUsername { get; } = "ops-viewer";
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
                "ALTER DATABASE [MSOSyncOperations_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();
        await CreateUserAsync(db, hasher, AdminUsername,  AdminPassword,  "ADMIN");
        await CreateUserAsync(db, hasher, ViewerUsername, ViewerPassword, "VIEWER");
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
            "ALTER DATABASE [MSOSyncOperations_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public async Task<HttpClient> AdminClientAsync()  => await MakeClientAsync(AdminUsername,  AdminPassword);
    public async Task<HttpClient> ViewerClientAsync() => await MakeClientAsync(ViewerUsername, ViewerPassword);

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

[CollectionDefinition("Operations")]
public sealed class OperationsCollection : ICollectionFixture<OperationsFixture> { }

// ── Tests ─────────────────────────────────────────────────────────────────────

[Collection("Operations")]
public sealed class OperationsIntegrationTests(OperationsFixture fixture)
    : IClassFixture<OperationsFixture>
{
    // ── List returns created operations ──────────────────────────────────────

    [Fact]
    public async Task GetOperations_AdminToken_Returns200WithItems()
    {
        using var scope = fixture.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IOperationService>();
        await svc.CreateAsync(OperationType.Export, null, null, OperationSource.Api,
            "list-test", false, false, "List test op", null, default);

        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync("api/v1/operations");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    // ── Cancel a running rollout operation ──────────────────────────────────

    [Fact]
    public async Task CancelRolloutOperation_UpdatesOperationStatusToCancelled()
    {
        using var scope = fixture.Services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IOperationService>();

        var rolloutId = Guid.NewGuid();
        db.ConfigurationRollouts.Add(new SyncConfigurationRollout
        {
            Id              = rolloutId,
            Status          = "InProgress",
            TemplateId      = Guid.NewGuid(),
            TemplateVersion = 1,
            TargetNodeCount = 1,
            InitiatedBy     = Guid.NewGuid(),
            StartedAt       = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var operationId = await svc.CreateAsync(
            OperationType.Rollout, rolloutId, null, OperationSource.User,
            rolloutId.ToString(), canCancel: true, canRetry: false,
            "Test rollout", null, default);

        // Act: cancel via API
        var client = await fixture.AdminClientAsync();
        var resp = await client.PostAsJsonAsync($"api/v1/operations/{operationId}/cancel", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: operation is Cancelled
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Cancelled");

        var op = await db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.OperationId == operationId);
        op!.Status.Should().Be("Cancelled");
        op.Result.Should().Be("Cancelled");
        op.CompletedAt.Should().NotBeNull();

        // The rollout row should also be Cancelled (via RolloutOperationHandler)
        var rollout = await db.ConfigurationRollouts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rolloutId);
        rollout!.Status.Should().Be("Cancelled");
    }

    // ── Viewer cannot cancel ──────────────────────────────────────────────────

    [Fact]
    public async Task CancelOperation_ViewerToken_Returns403()
    {
        using var scope = fixture.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IOperationService>();

        var opId = await svc.CreateAsync(
            OperationType.Export, Guid.NewGuid(), null, OperationSource.User,
            "viewer-cancel-test", canCancel: true, canRetry: false,
            "Viewer cancel test", null, default);

        var client = await fixture.ViewerClientAsync();
        var resp = await client.PostAsJsonAsync($"api/v1/operations/{opId}/cancel", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
