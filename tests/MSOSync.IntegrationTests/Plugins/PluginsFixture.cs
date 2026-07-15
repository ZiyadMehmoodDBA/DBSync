// tests/MSOSync.IntegrationTests/Plugins/PluginsFixture.cs
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
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Stores;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Diagnostics;
using MSOSync.Plugin.Hosting;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.Plugins;

public sealed class PluginsFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncPlugins_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    // TestAssets plugins directory — the loader will scan this at startup
    public static readonly string TestPluginsPath =
        Path.Combine(
            Path.GetDirectoryName(typeof(PluginsFixture).Assembly.Location)!,
            "TestAssets", "plugins");

    public string AdminUsername { get; } = "plugin-admin";
    public string AdminPassword { get; } = "AdminP@ss1!";

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

        // Plugin host wiring
        testBuilder.Services.Configure<PluginHostOptions>(opts =>
        {
            opts.PluginsPath = TestPluginsPath;
            opts.HostVersion = "14.0.0";
        });
        testBuilder.Services.AddSingleton<IPluginRegistry, PluginRegistry>();
        testBuilder.Services.AddScoped<IPluginStore, PluginStore>();
        testBuilder.Services.AddSingleton<IPluginLoader, PluginLoader>();
        testBuilder.Services.AddSingleton<PluginHost>();
        testBuilder.Services.AddSingleton<IPluginHost>(sp =>
            sp.GetRequiredService<PluginHost>());
        testBuilder.Services.AddHostedService(sp =>
            sp.GetRequiredService<PluginHost>());
        testBuilder.Services.AddHealthChecks()
            .AddCheck<PluginHealthCheck>("plugins");

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
                "ALTER DATABASE [MSOSyncPlugins_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
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
        var user   = new SyncUser
        {
            Username     = AdminUsername,
            PasswordHash = hasher.Hash(AdminPassword),
            Enabled      = true,
            CreatedTime  = DateTime.UtcNow,
        };
        db.Users.Add(user);
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
                "ALTER DATABASE [MSOSyncPlugins_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }

    public async Task<HttpClient> AdminClientAsync()
    {
        var loginClient = CreateClient();
        var resp = await loginClient.PostAsJsonAsync("/api/v1/auth/login",
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

[CollectionDefinition("Plugins")]
public sealed class PluginsCollection : ICollectionFixture<PluginsFixture> { }
