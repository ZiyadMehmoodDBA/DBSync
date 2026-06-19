# Epic 4 / Task 11: Metadata Integration Tests (Testcontainers)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `MetadataFixture` and `MetadataTests` to the existing `MSOSync.IntegrationTests` project. Tests hit real SQL Server (via Testcontainers), exercise the full HTTP pipeline through `WebApplicationFactory`, and verify key behaviors: CRUD round-trips, history writes, token verification, exception envelope shape.

**Architecture:** `MetadataFixture` follows the same `CreateHost` override pattern as `SecurityFixture` — builds a `WebApplication` from scratch via `testBuilder` rather than using `HostFactoryResolver`. Adds `AddMetadata`, `AddExceptionHandler<GlobalExceptionHandler>`, `AddProblemDetails`, and `app.UseExceptionHandler()`. Existing `SecurityFixture` remains untouched.

**Tech Stack:** Testcontainers.MsSql 4.4.0, xUnit 2.9.3, FluentAssertions 6.12.2, Microsoft.AspNetCore.Mvc.Testing 9.0.0

## Global Constraints

- `MSOSync.IntegrationTests.csproj` must reference `MSOSync.Metadata` and `MSOSync.Api` projects
- `MetadataFixture` uses `[CollectionDefinition("Metadata")]` / `ICollectionFixture<MetadataFixture>`
- `MetadataTests` uses `[Collection("Metadata")]`
- Container image: `"mcr.microsoft.com/mssql/server:2022-latest"`
- Fixture seeds: one `SyncParameter` row (`"sync.batch.size"` = `"100"`), one `SyncChannel`, one `SyncNodeGroup`, roles (ADMIN/OPERATOR/VIEWER), one admin user
- Admin JWT obtained via `POST /api/v1/auth/login` in a helper
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Modify: `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj` — add project references for Metadata + Api
- Create: `tests/MSOSync.IntegrationTests/Metadata/MetadataFixture.cs`
- Create: `tests/MSOSync.IntegrationTests/Metadata/MetadataTests.cs`

**Interfaces:**
- Consumes: `AddMetadata()` (Task 8), `GlobalExceptionHandler` (Task 3), all 6 controllers (Task 9), `SecurityFixture` pattern (existing)
- Produces: Full integration test suite — CI gate

---

- [ ] **Step 1: Update `MSOSync.IntegrationTests.csproj`**

Add project references for `MSOSync.Metadata` and `MSOSync.Api`. Full file after edit:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.MsSql" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.App\MSOSync.App.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Api\MSOSync.Api.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `MetadataFixture`**

```csharp
// tests/MSOSync.IntegrationTests/Metadata/MetadataFixture.cs
using DotNet.Testcontainers.Builders;
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

        // Seed roles
        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        // Seed admin user
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

        // Seed test data
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
```

- [ ] **Step 3: Create `MetadataTests`**

```csharp
// tests/MSOSync.IntegrationTests/Metadata/MetadataTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Metadata;

[Collection("Metadata")]
public sealed class MetadataTests(MetadataFixture factory)
{
    private HttpClient Client() => factory.CreateClient();

    private async Task<string> GetAdminTokenAsync()
    {
        var client = Client();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = factory.AdminUsername,
            Password = factory.AdminPassword
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginBody>();
        return body!.Token;
    }

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var token = await GetAdminTokenAsync();
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Parameters ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ParameterUpdate_WritesHistoryRow_InDatabase()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.PutAsJsonAsync(
            "/api/v1/parameters/sync.batch.size",
            new { Value = "500" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var histResp = await client.GetAsync("/api/v1/parameters/sync.batch.size/history");
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hist = await histResp.Content.ReadFromJsonAsync<List<HistoryItem>>();
        hist.Should().NotBeEmpty();
        hist![0].NewValue.Should().Be("500");
    }

    [Fact]
    public async Task ParameterUpdate_UnknownName_Returns404()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.PutAsJsonAsync(
            "/api/v1/parameters/no.such.param",
            new { Value = "x" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Code.Should().Be("PARAMETER_NOT_FOUND");
    }

    // ── Triggers ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerCrud_CreateGetUpdateDelete_RoundTrip()
    {
        var client = await AuthorizedClientAsync();

        // Create
        var createResp = await client.PostAsJsonAsync("/api/v1/triggers", new
        {
            TriggerId = "t-int-1",
            SourceTable = "dbo.Orders",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = true,
            SyncOnDelete = false
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Get
        var getResp = await client.GetAsync("/api/v1/triggers/t-int-1");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var trigger = await getResp.Content.ReadFromJsonAsync<TriggerItem>();
        trigger!.TriggerId.Should().Be("t-int-1");
        trigger.TriggerVersion.Should().Be(1);

        // Update
        var updateResp = await client.PutAsJsonAsync("/api/v1/triggers/t-int-1", new
        {
            SourceTable = "dbo.OrdersV2",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = false,
            SyncOnDelete = false
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<TriggerItem>();
        updated!.TriggerVersion.Should().Be(2);
        updated.SourceTable.Should().Be("dbo.OrdersV2");

        // Delete
        var deleteResp = await client.DeleteAsync("/api/v1/triggers/t-int-1");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getDeleted = await client.GetAsync("/api/v1/triggers/t-int-1");
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicateTrigger_Returns409Conflict()
    {
        var client = await AuthorizedClientAsync();

        await client.PostAsJsonAsync("/api/v1/triggers", new
        {
            TriggerId = "t-dup-int",
            SourceTable = "dbo.X",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = true,
            SyncOnDelete = true
        });

        var resp = await client.PostAsJsonAsync("/api/v1/triggers", new
        {
            TriggerId = "t-dup-int",
            SourceTable = "dbo.X",
            ChannelId = "default",
            SyncOnInsert = true,
            SyncOnUpdate = true,
            SyncOnDelete = true
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Code.Should().Be("DUPLICATE_TRIGGER");
    }

    // ── Channels ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelCrud_CreateGetUpdateDelete_RoundTrip()
    {
        var client = await AuthorizedClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/channels", new
        {
            ChannelId = "ch-int-1",
            Priority = 5,
            BatchSize = 500,
            MaxBatchToSend = 5,
            MaxDataSize = 2097152
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResp = await client.GetAsync("/api/v1/channels/ch-int-1");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var channel = await getResp.Content.ReadFromJsonAsync<ChannelItem>();
        channel!.ChannelId.Should().Be("ch-int-1");
        channel.BatchSize.Should().Be(500);

        var updateResp = await client.PutAsJsonAsync("/api/v1/channels/ch-int-1", new
        {
            Priority = 10,
            BatchSize = 250,
            MaxBatchToSend = 3,
            MaxDataSize = 1048576
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResp = await client.DeleteAsync("/api/v1/channels/ch-int-1");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Exception Handler ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExceptionHandler_NotFound_Returns404WithEnvelope()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.GetAsync("/api/v1/parameters/does-not-exist");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.Status.Should().Be(404);
        body.Code.Should().NotBeNullOrEmpty();
        body.Message.Should().NotBeNullOrEmpty();
        body.CorrelationId.Should().NotBeNullOrEmpty();
    }

    // ── Routers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouterSourceGroup_FiltersCorrectly()
    {
        var client = await AuthorizedClientAsync();

        await client.PostAsJsonAsync("/api/v1/routers", new
        {
            RouterId = "r-int-src",
            SourceNodeGroup = "src-group",
            TargetNodeGroup = "tgt-group",
            RouterType = "default"
        });

        await client.PostAsJsonAsync("/api/v1/routers", new
        {
            RouterId = "r-int-other",
            SourceNodeGroup = "other-group",
            TargetNodeGroup = "tgt-group",
            RouterType = "default"
        });

        var resp = await client.GetAsync("/api/v1/routers/source/src-group");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var routers = await resp.Content.ReadFromJsonAsync<List<RouterItem>>();
        routers.Should().ContainSingle(r => r.RouterId == "r-int-src");
        routers.Should().NotContain(r => r.RouterId == "r-int-other");
    }

    // ── Metadata Summary ──────────────────────────────────────────────────────

    [Fact]
    public async Task MetadataSummary_ReturnsCountsObject()
    {
        var client = await AuthorizedClientAsync();

        var resp = await client.GetAsync("/api/v1/metadata");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<MetadataSummary>();
        body.Should().NotBeNull();
        body!.Parameters.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── Helper record types ───────────────────────────────────────────────────

    private sealed record LoginBody(string Token, string RefreshToken);
    private sealed record ErrorEnvelope(int Status, string Error, string Code, string Message, string CorrelationId);
    private sealed record HistoryItem(string ParameterName, string? OldValue, string? NewValue);
    private sealed record TriggerItem(string TriggerId, string SourceTable, int TriggerVersion);
    private sealed record ChannelItem(string ChannelId, int BatchSize);
    private sealed record RouterItem(string RouterId, string SourceNodeGroup);
    private sealed record MetadataSummary(int Nodes, int Triggers, int Routers, int Channels, int Parameters);
}
```

- [ ] **Step 4: Build `MSOSync.IntegrationTests` to verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 5: Run metadata integration tests**

```powershell
dotnet test tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj --filter "Collection=Metadata" --logger "console;verbosity=normal"
```

Expected: all Metadata collection tests pass. (Docker must be running for Testcontainers.)

- [ ] **Step 6: Run full test suite**

```powershell
dotnet test MSOSync.sln
```

Expected: all tests pass including Security and ArchTest collections.

- [ ] **Step 7: Commit**

```powershell
git add tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj
git add tests/MSOSync.IntegrationTests/Metadata/MetadataFixture.cs
git add tests/MSOSync.IntegrationTests/Metadata/MetadataTests.cs
git commit -m "test(integration): add MetadataFixture and MetadataTests with Testcontainers SQL Server"
```
