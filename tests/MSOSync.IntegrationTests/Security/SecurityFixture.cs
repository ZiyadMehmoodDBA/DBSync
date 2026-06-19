// tests/MSOSync.IntegrationTests/Security/SecurityFixture.cs
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
using MSOSync.App;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.IntegrationTests.Security;

public sealed class SecurityFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncSecurity_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername { get; } = "testadmin";
    public string AdminPassword { get; } = "TestP@ss1!";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Build the app directly from the same DI setup as Program.cs,
        // bypassing HostFactoryResolver to avoid issues with the return-exitCode pattern.
        var testBuilder = WebApplication.CreateBuilder();

        testBuilder.WebHost.UseTestServer();

        // Inject test configuration
        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnStr,
            ["Jwt:Secret"] = JwtSecret,
        });

        testBuilder.Environment.EnvironmentName = "Test";

        testBuilder.Services.AddEndpointsApiExplorer();
        testBuilder.Services.AddSwaggerGen();
        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        testBuilder.Services.AddFluentValidationAutoValidation();
        testBuilder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

        testBuilder.Services.AddHostedService<AdminBootstrapper>();

        var app = testBuilder.Build();

        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseNodeTokenAuth();
        app.UseAuthorization();

        app.MapControllers();

        app.MapGet("/health", () => Results.Ok(new { status = "UP", version = "0.1.0" }))
           .WithName("Health")
           .WithTags("System");

        app.Start();

        return app;
    }

    public async Task InitializeAsync()
    {
        // Migrate and seed outside the app pipeline so AdminBootstrapper is a no-op.
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr)
            .Options;

        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();

        if (!await db.Roles.AnyAsync(r => r.RoleName == "ADMIN"))
            db.Roles.Add(new SyncRole { RoleName = "ADMIN" });

        if (!await db.Roles.AnyAsync(r => r.RoleName == "OPERATOR"))
            db.Roles.Add(new SyncRole { RoleName = "OPERATOR" });

        if (!await db.Roles.AnyAsync(r => r.RoleName == "VIEWER"))
            db.Roles.Add(new SyncRole { RoleName = "VIEWER" });

        await db.SaveChangesAsync();

        if (!await db.Users.AnyAsync(u => u.Username == AdminUsername))
        {
            var hasher = new BCryptPasswordHasher();
            var user = new SyncUser
            {
                Username = AdminUsername,
                PasswordHash = hasher.Hash(AdminPassword),
                Enabled = true,
                CreatedTime = DateTime.UtcNow
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
            .UseSqlServer(ConnStr)
            .Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncSecurity_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Security")]
public sealed class SecurityCollection : ICollectionFixture<SecurityFixture> { }
