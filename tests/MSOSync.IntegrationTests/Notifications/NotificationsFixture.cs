// tests/MSOSync.IntegrationTests/Notifications/NotificationsFixture.cs
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
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.Notifications;

public sealed class NotificationsFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncNotifications_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername  { get; } = "notif-admin";
    public string AdminPassword  { get; } = "AdminP@ss1!";
    public string ViewerUsername { get; } = "notif-viewer";
    public string ViewerPassword { get; } = "ViewerP@ss1!";

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

        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncNotifications_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
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
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncNotifications_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }

    public async Task<HttpClient> AdminClientAsync()  => await MakeClientAsync(AdminUsername,  AdminPassword);
    public async Task<HttpClient> ViewerClientAsync() => await MakeClientAsync(ViewerUsername, ViewerPassword);
    public HttpClient AnonClient()                    => CreateClient();

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

[CollectionDefinition("Notifications")]
public sealed class NotificationsCollection : ICollectionFixture<NotificationsFixture> { }
