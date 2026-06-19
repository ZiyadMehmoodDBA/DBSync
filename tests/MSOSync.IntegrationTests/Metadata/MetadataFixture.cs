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
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Metadata;

public sealed class MetadataFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string AdminUsername { get; } = "metaadmin";
    public string AdminPassword { get; } = "MetaP@ss1!";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var connStr = _container.GetConnectionString();
        var testBuilder = WebApplication.CreateBuilder();

        testBuilder.WebHost.UseTestServer();

        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connStr,
            ["Jwt:Secret"] = JwtSecret,
        });

        testBuilder.Environment.EnvironmentName = "Test";

        testBuilder.Services.AddEndpointsApiExplorer();
        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddHttpContextAccessor();
        testBuilder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();
        testBuilder.Services.AddMetadata(testBuilder.Configuration);

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        testBuilder.Services.AddFluentValidationAutoValidation();
        testBuilder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

        testBuilder.Services.AddHostedService<AdminBootstrapper>();

        var app = testBuilder.Build();

        app.UseExceptionHandler();
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
        await _container.StartAsync();

        var connStr = _container.GetConnectionString();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connStr)
            .Options;

        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
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

        if (!await db.Parameters.AnyAsync(p => p.ParameterName == "sync.batch.size"))
        {
            db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
            await db.SaveChangesAsync();
        }

        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == "default"))
        {
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = "default", GroupName = "Default Group" });
            await db.SaveChangesAsync();
        }

        if (!await db.Channels.AnyAsync(c => c.ChannelId == "default"))
        {
            db.Channels.Add(new SyncChannel
            {
                ChannelId = "default", Priority = 1,
                BatchSize = 1000, MaxBatchToSend = 10, MaxDataSize = 1048576L
            });
            await db.SaveChangesAsync();
        }
    }

    public new async Task DisposeAsync()
    {
        await _container.StopAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("Metadata")]
public sealed class MetadataCollection : ICollectionFixture<MetadataFixture> { }
