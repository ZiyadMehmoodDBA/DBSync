// tests/MSOSync.IntegrationTests/Users/UsersFixture.cs
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
using System.Net.Http.Json;
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

namespace MSOSync.IntegrationTests.Users;

public sealed class UsersFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncUsers_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername { get; } = "testadmin";
    public string AdminPassword { get; } = "TestP@ss1!";

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
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();

        if (!await db.Roles.AnyAsync(r => r.RoleName == "ADMIN"))
            db.Roles.Add(new SyncRole { RoleName = "ADMIN" });
        await db.SaveChangesAsync();

        if (!await db.Users.AnyAsync(u => u.Username == AdminUsername))
        {
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
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncUsers_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public async Task<string> LoginAdminAsync()
    {
        var client = CreateClient();
        var resp   = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { Username = AdminUsername, Password = AdminPassword });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TokenBody>();
        return body!.Token;
    }

    private sealed record TokenBody(string Token, string RefreshToken, DateTime ExpiresAt);
}

[CollectionDefinition("Users")]
public sealed class UsersCollection : ICollectionFixture<UsersFixture> { }
