// tests/MSOSync.IntegrationTests/Marketplace/MarketplaceFixture.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.Marketplace;

/// <summary>
/// Fake HTTP handler that captures and serves canned responses for the
/// MarketplaceRegistry named client during integration tests.
/// </summary>
public sealed class FakeRegistryHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string         _content    = "{}";
    public int CallCount { get; private set; }

    public void SetResponse(HttpStatusCode status, object payload)
    {
        _statusCode = status;
        _content    = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public void SetError(HttpStatusCode status) =>
        (_statusCode, _content) = (status, "Not Found");

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

public sealed class MarketplaceFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncMarketplace_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";
    private const string RegistryUrl = "https://registry.fake.test";

    public string AdminUsername { get; } = "mkt-admin";
    public string AdminPassword { get; } = "AdminP@ss1!";

    /// <summary>
    /// Fixture with RegistryUrl configured — marketplace endpoints are active.
    /// </summary>
    public MarketplaceFixture() { }

    public static readonly string TestPluginsPath =
        Path.Combine(
            Path.GetDirectoryName(typeof(MarketplaceFixture).Assembly.Location)!,
            "TestAssets", "plugins");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var fakeHandler = new FakeRegistryHandler();
        fakeHandler.SetResponse(HttpStatusCode.OK,
            new { data = Array.Empty<object>(), total = 0, page = 1, pageSize = 20, totalPages = 0 });

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
            ["Marketplace:RegistryUrl"]             = RegistryUrl,
            ["Marketplace:CacheMinutes"]            = "60",
            ["Marketplace:MemoryCacheMinutes"]      = "5",
            ["Marketplace:HttpTimeoutSeconds"]      = "10",
            ["Marketplace:RetryCount"]              = "1",
        });

        RegisterCommonServices(testBuilder.Services, testBuilder.Configuration, fakeHandler);

        var app = testBuilder.Build();
        ConfigurePipeline(app);
        app.Start();
        return app;
    }

    private static void RegisterCommonServices(
        IServiceCollection services,
        IConfiguration config,
        FakeRegistryHandler? fakeHandler = null)
    {
        services.AddPersistence(config);
        services.AddSecurity(config);
        services.AddMetadata(config);
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

        // Plugin host wiring
        services.Configure<PluginHostOptions>(opts =>
        {
            opts.PluginsPath = TestPluginsPath;
            opts.HostVersion = "1.0.0";
        });
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<IPluginRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddScoped<IPluginStore, MSOSync.Persistence.Stores.PluginStore>();
        services.AddSingleton<IPluginLoader, PluginLoader>();
        services.AddPluginCoreInternals();
        services.AddPluginPackaging(config);
        services.AddSingleton<PluginHost>();
        services.AddSingleton<IPluginHost>(sp => sp.GetRequiredService<PluginHost>());
        services.AddHostedService(sp => sp.GetRequiredService<PluginHost>());
        services.AddHealthChecks()
            .AddCheck<PluginHealthCheck>("plugins");

        // Marketplace services + named HTTP client
        services.AddMarketplace(config);

        // Override the named HTTP client with our fake handler if provided
        if (fakeHandler is not null)
        {
            services.AddHttpClient("MarketplaceRegistry")
                .ConfigurePrimaryHttpMessageHandler(() => fakeHandler);
        }
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseNodeTokenAuth();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health/ready");
    }

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        await using var db = new AppDbContext(opts);

        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncMarketplace_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
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
                "ALTER DATABASE [MSOSyncMarketplace_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
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

    public HttpClient AnonClient() => CreateClient();
}

[CollectionDefinition("Marketplace")]
public sealed class MarketplaceCollection : ICollectionFixture<MarketplaceFixture> { }
