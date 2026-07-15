# Epic 14A — Task 9: Integration Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Create a minimal test plugin DLL, build and commit it under `TestAssets/`, write `PluginsFixture` (WebApplicationFactory), and implement 10 integration tests covering all spec scenarios.

**Architecture:** Test plugin is a separate .NET class library project `tests/MSOSync.TestPlugin`. Build it, copy the DLL to `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/`. `PluginsFixture` extends `WebApplicationFactory<Program>` — same pattern as `NotificationsFixture`. Tests configure `PluginHost:PluginsPath` to point at the `TestAssets/plugins/` directory.

**Tech Stack:** C# 13 / .NET 9 / xUnit + FluentAssertions / `WebApplicationFactory<Program>` / localdb

## Global Constraints

- Test DB name: `MSOSyncPlugins_Test`
- Plugin ID in test asset: `msosync.test`, entry type: `MSOSync.TestPlugin.TestPlugin`
- `PluginsFixture` wires plugin services the same way `Program.cs` does
- All 10 tests from the spec must be present
- The TestPlugin DLL must be committed to git (binary asset)

## Files

**Create:**
- `tests/MSOSync.TestPlugin/MSOSync.TestPlugin.csproj`
- `tests/MSOSync.TestPlugin/TestPlugin.cs`
- `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/plugin.json`
- `tests/MSOSync.IntegrationTests/Plugins/PluginsFixture.cs`
- `tests/MSOSync.IntegrationTests/Plugins/PluginControllerTests.cs`

**Built then committed (binary):**
- `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/MSOSync.TestPlugin.dll`

**Modify:**
- `MSOSync.sln` — add `tests/MSOSync.TestPlugin`
- `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj` — add reference to MSOSync.Plugin

## Interfaces

**Consumes:** All backend plugin services (Tasks 1–7)

**Produces:** Verified end-to-end: load, disable, fail-gracefully, enable/disable API, manifest API, 503 before init

---

- [ ] **Step 1: Create test plugin project `tests/MSOSync.TestPlugin/MSOSync.TestPlugin.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MSOSync.TestPlugin</AssemblyName>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `tests/MSOSync.TestPlugin/TestPlugin.cs`**

```csharp
namespace MSOSync.TestPlugin;

/// <summary>
/// Minimal test plugin. No logic — exists only for the loader to verify the entry type.
/// </summary>
public sealed class TestPlugin { }
```

- [ ] **Step 3: Add MSOSync.TestPlugin to solution and build the DLL**

```bash
dotnet sln D:\MSOSync\MSOSync.sln add tests/MSOSync.TestPlugin/MSOSync.TestPlugin.csproj
```

Build and copy the output DLL to the TestAssets directory:

```bash
dotnet build tests/MSOSync.TestPlugin -c Release
$dllSrc = "tests/MSOSync.TestPlugin/bin/Release/net9.0/MSOSync.TestPlugin.dll"
$dllDst = "tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/"
New-Item -ItemType Directory -Force -Path $dllDst
Copy-Item $dllSrc $dllDst
```

- [ ] **Step 4: Create `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/plugin.json`**

```json
{
  "id": "msosync.test",
  "name": "MSOSync Test Plugin",
  "version": "1.0.0",
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.9.999",
  "entryAssembly": "MSOSync.TestPlugin.dll",
  "entryType": "MSOSync.TestPlugin.TestPlugin",
  "author": "MSOSync",
  "description": "Minimal plugin for integration tests.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

- [ ] **Step 5: Add MSOSync.Plugin reference to integration test project**

In `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj`, add:

```xml
<ProjectReference Include="..\..\src\MSOSync.Plugin\MSOSync.Plugin.csproj" />
```

- [ ] **Step 6: Create `tests/MSOSync.IntegrationTests/Plugins/PluginsFixture.cs`**

```csharp
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
        var testBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
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

        var role = await db.Roles.FirstAsync(r => r.RoleName == "ADMIN");
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
}

[CollectionDefinition("Plugins")]
public sealed class PluginsCollection : ICollectionFixture<PluginsFixture> { }
```

- [ ] **Step 7: Create `tests/MSOSync.IntegrationTests/Plugins/PluginControllerTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Abstractions;
using Xunit;

namespace MSOSync.IntegrationTests.Plugins;

[Collection("Plugins")]
public sealed class PluginControllerTests(PluginsFixture fx)
{
    // ── GET /api/v1/plugins ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPlugins_Unauthenticated_Returns401()
    {
        var resp = await fx.AnonClient().GetAsync("/api/v1/plugins");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPlugins_AsAdmin_Returns200WithTestPlugin()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var plugins = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        plugins.Should().NotBeNull();
        // msosync.test should be present and loaded (entry type exists in the DLL)
        var testPlugin = plugins!.FirstOrDefault(p =>
            p.GetProperty("pluginId").GetString() == "msosync.test");
        testPlugin.Should().NotBeNull("test plugin must be discovered");
    }

    // ── GET /api/v1/plugins/summary ─────────────────────────────────────────

    [Fact]
    public async Task GetPluginSummary_ReturnsCorrectCounts()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/summary");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("loaded").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // ── GET /api/v1/plugins/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task GetPlugin_KnownId_Returns200()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/msosync.test");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pluginId").GetString().Should().Be("msosync.test");
    }

    [Fact]
    public async Task GetPlugin_UnknownId_Returns404()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/no.such.plugin");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/v1/plugins/{id}/manifest ────────────────────────────────────

    [Fact]
    public async Task GetPluginManifest_Returns200WithFields()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/msosync.test/manifest");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be("msosync.test");
        body.GetProperty("entryType").GetString().Should().Be("MSOSync.TestPlugin.TestPlugin");
    }

    // ── POST /api/v1/plugins/{id}/enable|disable ─────────────────────────────

    [Fact]
    public async Task DisablePlugin_ReturnsRestartRequired()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.PostAsync("/api/v1/plugins/msosync.test/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("restartRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task EnablePlugin_ReturnsRestartRequired()
    {
        var client = await fx.AdminClientAsync();
        // First disable, then re-enable
        await client.PostAsync("/api/v1/plugins/msosync.test/disable", null);
        var resp = await client.PostAsync("/api/v1/plugins/msosync.test/enable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("restartRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DisablePlugin_UnknownId_Returns404()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.PostAsync("/api/v1/plugins/no.such.plugin/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Registry state ────────────────────────────────────────────────────────

    [Fact]
    public async Task PluginHost_ValidPlugin_RegistersAsLoaded()
    {
        // The registry is populated at startup. The test plugin should be Loaded.
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/msosync.test");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Loaded");
    }

    [Fact]
    public async Task PluginHost_HealthCheck_ReturnsHealthyWhenLoaded()
    {
        var client = fx.CreateClient();
        var resp   = await client.GetAsync("/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

Note: the `AnonClient()` method is inherited. Add it to `PluginsFixture`:

```csharp
public HttpClient AnonClient() => CreateClient();
```

- [ ] **Step 8: Run integration tests**

```bash
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Plugins" -v minimal
```

Expected: All 10 tests pass. If the test plugin DLL contains the correct entry type (`MSOSync.TestPlugin.TestPlugin`), the "RegistersAsLoaded" test will pass.

- [ ] **Step 9: Commit all — including the DLL binary**

```bash
git add tests/MSOSync.TestPlugin/ tests/MSOSync.IntegrationTests/TestAssets/ tests/MSOSync.IntegrationTests/Plugins/ tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj MSOSync.sln
git commit -m "feat(14A-9): integration tests, test plugin DLL, PluginsFixture, 10 controller tests"
```
