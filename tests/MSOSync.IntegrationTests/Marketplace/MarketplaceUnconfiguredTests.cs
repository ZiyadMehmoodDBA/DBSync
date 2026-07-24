// tests/MSOSync.IntegrationTests/Marketplace/MarketplaceUnconfiguredTests.cs
// Tests marketplace endpoints when Marketplace:RegistryUrl is not configured.
// All endpoints must return 503 Service Unavailable (except auth guard → 401).
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
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
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Diagnostics;
using MSOSync.Plugin.Hosting;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Registry;
using MSOSync.Security;
using MSOSync.Topology;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Marketplace;

/// <summary>
/// WebApplicationFactory for testing marketplace endpoints when RegistryUrl is null.
/// </summary>
public sealed class MarketplaceUnconfiguredFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncMarketplaceUnconf_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername { get; } = "mktunconf-admin";
    public string AdminPassword { get; } = "AdminP@ss1!";

    private static readonly string TestPluginsPath =
        Path.Combine(
            Path.GetDirectoryName(typeof(MarketplaceUnconfiguredFixture).Assembly.Location)!,
            "TestAssets", "plugins");

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
            ["Pagination:CursorHmacKey"]            = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["PluginHost:PluginsPath"]              = TestPluginsPath,
            // Marketplace:RegistryUrl deliberately omitted → IsConfigured = false
        });

        var services = testBuilder.Services;
        services.AddPersistence(testBuilder.Configuration);
        services.AddSecurity(testBuilder.Configuration);
        services.AddMetadata(testBuilder.Configuration);
        services.AddSingleton<IClock, SystemClock>();
        services.AddTopologyServices();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<MSOSync.App.SignalR.NodeOperationsPublisher>());
        services.AddSignalR();
        services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly)
            .AddJsonOptions(opts =>
                opts.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter()));
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<AuthController>();
        services.Configure<MSOSync.Plugin.Models.PluginHostOptions>(opts =>
        {
            opts.PluginsPath = TestPluginsPath;
            opts.HostVersion = "1.0.0";
        });
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<IPluginRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddScoped<IPluginStore, MSOSync.Persistence.Stores.PluginStore>();
        services.AddSingleton<IPluginLoader, PluginLoader>();
        services.AddPluginCoreInternals();
        services.AddPluginPackaging(testBuilder.Configuration);
        services.AddSingleton<PluginHost>();
        services.AddSingleton<IPluginHost>(sp => sp.GetRequiredService<PluginHost>());
        services.AddHostedService(sp => sp.GetRequiredService<PluginHost>());
        services.AddHealthChecks()
            .AddCheck<PluginHealthCheck>("plugins");

        // Marketplace (no RegistryUrl → IsConfigured = false)
        services.AddMarketplace(testBuilder.Configuration);

        var app = testBuilder.Build();
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseNodeTokenAuth();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health/ready");
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
                "ALTER DATABASE [MSOSyncMarketplaceUnconf_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        await db.Database.MigrateAsync();

        var hasher = new BCryptPasswordHasher();
        var user   = new SyncUser
        {
            Username     = AdminUsername,
            PasswordHash = hasher.Hash(AdminPassword),
            Enabled      = true,
            CreatedTime  = DateTime.UtcNow,
        };
        db.Users.Add(user);

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        var adminRole = await db.Roles.FirstAsync(r => r.RoleName == "ADMIN");
        db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = adminRole.RoleId });
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncMarketplaceUnconf_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }

    public async Task<HttpClient> AdminClientAsync()
    {
        var loginClient = CreateClient();
        var resp = await loginClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = AdminUsername, password = AdminPassword });
        resp.EnsureSuccessStatusCode();
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var c     = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}

[CollectionDefinition("MarketplaceUnconfigured")]
public sealed class MarketplaceUnconfiguredCollection
    : ICollectionFixture<MarketplaceUnconfiguredFixture> { }

[Collection("MarketplaceUnconfigured")]
public sealed class MarketplaceUnconfiguredTests(MarketplaceUnconfiguredFixture fx)
{
    [Fact]
    public async Task Search_NoRegistryUrl_Returns503()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins?page=1&pageSize=20");
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetPlugin_NoRegistryUrl_Returns503()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/some.plugin");
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetVersions_NoRegistryUrl_Returns503()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/some.plugin/versions");
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Install_NoRegistryUrl_Returns503()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.PostAsJsonAsync(
            "/api/v1/marketplace/plugins/some.plugin/install", new { version = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CheckUpdate_NoRegistryUrl_Returns503()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/some.plugin/updates");
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task BulkCheckUpdates_NoRegistryUrl_Returns503()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.PostAsJsonAsync(
            "/api/v1/marketplace/updates/check", new { updatesOnly = false });
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
