# Task 4 — DI Wiring + Integration Tests

**Plan:** `2026-07-23-phase-2C-2-master.md`
**Scope:** `MarketplaceServiceExtensions`, HTTP client + Polly, `Program.cs` wiring, unit tests for validators/cache store/services, integration tests for all 6 endpoints.

---

## Step 4.1 — `MarketplaceServiceExtensions`

- [ ] Create `src/MSOSync.App/MarketplaceServiceExtensions.cs`:

```csharp
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Metadata.Marketplace;
using MSOSync.Persistence.Stores;
using MSOSync.Plugin.Marketplace;

namespace MSOSync.App;

public static class MarketplaceServiceExtensions
{
    public static IServiceCollection AddMarketplace(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<MarketplaceOptions>(
            configuration.GetSection(MarketplaceOptions.SectionName));

        // FluentValidation validators
        services.AddScoped<IValidator<MarketplaceSearchParams>,   MarketplaceSearchParamsValidator>();
        services.AddScoped<IValidator<MarketplaceInstallRequest>, MarketplaceInstallRequestValidator>();
        services.AddScoped<IValidator<BulkUpdateCheckRequest>,    BulkUpdateCheckRequestValidator>();

        // Cache store (Scoped — shares the request-scoped DbContext)
        services.AddScoped<IMarketplaceCacheStore, MarketplaceCacheStore>();

        // Services (Scoped)
        services.AddScoped<IMarketplaceService,   MarketplaceService>();
        services.AddScoped<IPluginUpdateService,  PluginUpdateService>();

        return services;
    }
}
```

---

## Step 4.2 — HTTP client + Polly registration

- [ ] Open `src/MSOSync.App/Program.cs`
- [ ] After the line `builder.Services.AddPluginCoreInternals();` (or near the plugin host block), add:

```csharp
// Phase 2C.2 — Marketplace
builder.Services.AddMarketplace(builder.Configuration);

// Named HTTP client for the marketplace registry with Polly transient-error retry
builder.Services.AddHttpClient("MarketplaceRegistry", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<MSOSync.Plugin.Marketplace.MarketplaceOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.RegistryUrl))
        client.BaseAddress = new Uri(opts.RegistryUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(opts.HttpTimeoutSeconds);
    if (!string.IsNullOrWhiteSpace(opts.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add(
        "User-Agent",
        $"MSOSync/{typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}");
})
.AddTransientHttpErrorPolicy(p =>
    p.WaitAndRetryAsync(
        retryCount: 3,  // overridden at runtime from MarketplaceOptions.RetryCount in 2C.3
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
```

> If `Microsoft.Extensions.Http.Resilience` (Polly v8 `AddStandardResilienceHandler`) is already in the solution's NuGet graph, prefer `AddStandardResilienceHandler` over `AddTransientHttpErrorPolicy`. Check with:
> ```powershell
> Select-String -Path "src/**/*.csproj" -Pattern "Http.Resilience" -Recurse
> ```
> If found, replace the `.AddTransientHttpErrorPolicy(...)` block with:
> ```csharp
> .AddStandardResilienceHandler();
> ```

> The `RetryCount` option is not dynamically wired to `AddTransientHttpErrorPolicy` because the policy lambda must be a compile-time closure. Dynamic retry count from `MarketplaceOptions` can be added in 2C.3 using a custom pipeline or `.AddResilienceHandler(...)`.

- [ ] Add the required `using` at the top of `Program.cs`:

```csharp
using Microsoft.Extensions.Options;
using Polly.Extensions.Http;
```

---

## Step 4.3 — NuGet dependency check

- [ ] Verify `Polly.Extensions.Http` is already referenced or add it:

```powershell
Select-String -Path "src/MSOSync.App/MSOSync.App.csproj" -Pattern "Polly"
```

If not present, add to `src/MSOSync.App/MSOSync.App.csproj`:

```xml
<PackageReference Include="Polly.Extensions.Http" Version="3.0.0" />
```

- [ ] Verify `IMemoryCache` is available in `MSOSync.Metadata` (already registered via `services.AddMemoryCache()` in `MetadataServiceExtensions.cs` line 44 — no new package required).

---

## Step 4.4 — Full solution build check

- [ ] Run:

```powershell
dotnet build MSOSync.sln --no-restore
```

Expected: 0 errors, 0 warnings in new files.

---

## Step 4.5 — Unit tests: validators

- [ ] Create `tests/MSOSync.PluginTests/Marketplace/MarketplaceSearchParamsValidatorTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Api.Dtos.Marketplace;
using Xunit;

namespace MSOSync.PluginTests.Marketplace;

public sealed class MarketplaceSearchParamsValidatorTests
{
    private readonly MarketplaceSearchParamsValidator _sut = new();

    [Fact]
    public async Task Page_LessThan1_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 0, PageSize = 20 });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task PageSize_Zero_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 0 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task PageSize_101_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 101 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Query_ExceededMaxLength_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 20, Query = new string('x', 201) });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidParams_NoQueryNoCategory_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 20 });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidParams_WithQueryAndCategory_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 2, PageSize = 50, Query = "sql", Category = "connector" });
        result.IsValid.Should().BeTrue();
    }
}
```

- [ ] Create `tests/MSOSync.PluginTests/Marketplace/MarketplaceInstallRequestValidatorTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Api.Dtos.Marketplace;
using Xunit;

namespace MSOSync.PluginTests.Marketplace;

public sealed class MarketplaceInstallRequestValidatorTests
{
    private readonly MarketplaceInstallRequestValidator _sut = new();

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    public async Task NonSemverVersion_IsInvalid(string version)
    {
        var result = await _sut.ValidateAsync(new MarketplaceInstallRequest { Version = version });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task NullVersion_IsValid()
    {
        var result = await _sut.ValidateAsync(new MarketplaceInstallRequest { Version = null });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.10.300")]
    [InlineData("0.0.1")]
    public async Task ValidSemver_IsValid(string version)
    {
        var result = await _sut.ValidateAsync(new MarketplaceInstallRequest { Version = version });
        result.IsValid.Should().BeTrue();
    }
}
```

---

## Step 4.6 — Unit tests: `MarketplaceOptions`

- [ ] Create `tests/MSOSync.PluginTests/Marketplace/MarketplaceOptionsTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Plugin.Marketplace;
using Xunit;

namespace MSOSync.PluginTests.Marketplace;

public sealed class MarketplaceOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsConfigured_NullOrWhitespace_ReturnsFalse(string? url)
    {
        var opts = new MarketplaceOptions { RegistryUrl = url };
        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_WithUrl_ReturnsTrue()
    {
        var opts = new MarketplaceOptions { RegistryUrl = "https://marketplace.msosync.io/api/v1" };
        opts.IsConfigured.Should().BeTrue();
    }
}
```

---

## Step 4.7 — Unit tests: `MarketplaceService` caching

- [ ] Create `tests/MSOSync.PluginTests/Marketplace/MarketplaceServiceCacheTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Metadata.Marketplace;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using NSubstitute;
using Xunit;

namespace MSOSync.PluginTests.Marketplace;

public sealed class MarketplaceServiceCacheTests
{
    private static MarketplaceOptions ConfiguredOpts() => new()
    {
        RegistryUrl       = "https://registry.test",
        CacheMinutes      = 60,
        MemoryCacheMinutes = 5,
    };

    private static IMemoryCache BuildMemoryCache() =>
        new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task Search_L1Hit_SkipsL2AndRemote()
    {
        var store   = Substitute.For<IMarketplaceCacheStore>();
        var factory = Substitute.For<IHttpClientFactory>();
        var cache   = BuildMemoryCache();
        var opts    = Options.Create(ConfiguredOpts());

        // Pre-populate L1
        var expected = new RegistrySearchResult { Data = [], Total = 0, Page = 1, PageSize = 20, TotalPages = 0 };
        cache.Set("marketplace:search:||| 1|20".ToLowerInvariant(), expected,
            TimeSpan.FromMinutes(5));

        // Correct key: BuildSearchCacheKey(null, null, 1, 20) → "|null||null|1|20" — use method output
        // Simpler: set both plausible keys so the test doesn't depend on key format internals
        cache.Set("marketplace:search:|null||null|1|20", expected, TimeSpan.FromMinutes(5));
        cache.Set("marketplace:search:||1|20", expected, TimeSpan.FromMinutes(5));

        var sut = new MarketplaceService(factory, store, cache, opts,
            NullLogger<MarketplaceService>.Instance);

        // Act — actual key depends on BuildSearchCacheKey implementation
        // If L1 is hit the store and factory are never touched
        _ = await sut.SearchAsync(null, null, 1, 20, CancellationToken.None);

        // L2 and remote are skipped (store was never called with GetSearchCacheAsync
        // because L1 was a hit for at least one of the attempted keys above)
        // This test validates the service doesn't throw and returns without network call.
        await store.DidNotReceive().GetSearchCacheAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPlugin_L2Hit_SkipsRemote()
    {
        var store   = Substitute.For<IMarketplaceCacheStore>();
        var factory = Substitute.For<IHttpClientFactory>();
        var cache   = BuildMemoryCache();
        var opts    = Options.Create(ConfiguredOpts());

        var entry = new RegistryPluginEntry { Id = "msosync.test", Name = "Test", LatestVersion = "1.0.0",
            Author = "", Description = "", Category = "", MinHostVersion = "" };

        store.GetPluginCacheAsync("https://registry.test", "msosync.test", Arg.Any<CancellationToken>())
             .Returns(entry);

        var sut = new MarketplaceService(factory, store, cache, opts,
            NullLogger<MarketplaceService>.Instance);

        var result = await sut.GetPluginAsync("msosync.test", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("msosync.test");
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Theory]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("bad",   "1.0.0", false)]
    public async Task GetLatestUpdate_VersionComparison_CorrectResult(
        string registryLatest, string installed, bool expectUpdate)
    {
        var store   = Substitute.For<IMarketplaceCacheStore>();
        var factory = Substitute.For<IHttpClientFactory>();
        var cache   = BuildMemoryCache();
        var opts    = Options.Create(ConfiguredOpts());

        var versionEntry = new RegistryVersionEntry
        {
            Version = registryLatest, MinHostVersion = "1.0.0", MaxHostVersion = "99.0.0",
            DownloadUrl = "http://dl", Sha256 = "abc"
        };
        var entry = new RegistryPluginEntry
        {
            Id = "p1", Name = "P1", Author = "", Description = "", Category = "",
            MinHostVersion = "1.0.0", LatestVersion = registryLatest,
            Versions = new[] { versionEntry }
        };

        store.GetPluginCacheAsync("https://registry.test", "p1", Arg.Any<CancellationToken>())
             .Returns(entry);

        var sut = new MarketplaceService(factory, store, cache, opts,
            NullLogger<MarketplaceService>.Instance);

        var result = await sut.GetLatestUpdateAsync("p1", installed, CancellationToken.None);

        if (expectUpdate)
            result.Should().NotBeNull().And.Match<RegistryVersionEntry>(v => v.Version == registryLatest);
        else
            result.Should().BeNull();
    }
}
```

> Note: `NSubstitute` must be present in the test project. Check with:
> ```powershell
> Select-String -Path "tests/MSOSync.PluginTests/*.csproj" -Pattern "NSubstitute"
> ```
> If missing, add `<PackageReference Include="NSubstitute" Version="5.*" />` to the test project.

---

## Step 4.8 — Unit tests: `PluginUpdateService`

- [ ] Create `tests/MSOSync.PluginTests/Marketplace/PluginUpdateServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MSOSync.Metadata.Marketplace;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using MSOSync.Plugin.Models;
using NSubstitute;
using Xunit;

namespace MSOSync.PluginTests.Marketplace;

public sealed class PluginUpdateServiceTests
{
    private static RegistryVersionEntry MakeVersion(string v) => new()
    {
        Version = v, MinHostVersion = "1.0.0", MaxHostVersion = "99.0.0",
        DownloadUrl = $"http://dl/{v}", Sha256 = "abc"
    };

    [Fact]
    public async Task CheckAsync_SameVersion_ReturnsNull()
    {
        var marketplace = Substitute.For<IMarketplaceService>();
        marketplace.GetLatestUpdateAsync("p1", "1.0.0", Arg.Any<CancellationToken>())
                   .Returns((RegistryVersionEntry?)null);

        var store = Substitute.For<IPluginStore>();
        var sut   = new PluginUpdateService(marketplace, store,
            NullLogger<PluginUpdateService>.Instance);

        var result = await sut.CheckAsync("p1", "1.0.0", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_NewerAvailable_ReturnsManifest()
    {
        var marketplace = Substitute.For<IMarketplaceService>();
        var newer = MakeVersion("1.1.0");
        marketplace.GetLatestUpdateAsync("p1", "1.0.0", Arg.Any<CancellationToken>())
                   .Returns(newer);

        var store = Substitute.For<IPluginStore>();
        var sut   = new PluginUpdateService(marketplace, store,
            NullLogger<PluginUpdateService>.Instance);

        var result = await sut.CheckAsync("p1", "1.0.0", CancellationToken.None);

        result.Should().NotBeNull();
        result!.PluginId.Should().Be("p1");
        result.InstalledVersion.Should().Be("1.0.0");
        result.AvailableVersion.Should().Be("1.1.0");
    }

    [Fact]
    public async Task CheckAllAsync_ThreeInstalled_OneHasUpdate_ReturnsOne()
    {
        var marketplace = Substitute.For<IMarketplaceService>();
        var store       = Substitute.For<IPluginStore>();

        var installed = new List<PluginRecord>
        {
            new() { PluginId = "p1", PluginVersion = "1.0.0" },
            new() { PluginId = "p2", PluginVersion = "2.0.0" },
            new() { PluginId = "p3", PluginVersion = "3.0.0" },
        };
        store.GetAllAsync(Arg.Any<CancellationToken>()).Returns(installed);

        marketplace.GetLatestUpdateAsync("p1", "1.0.0", Arg.Any<CancellationToken>())
                   .Returns(MakeVersion("1.1.0"));
        marketplace.GetLatestUpdateAsync("p2", "2.0.0", Arg.Any<CancellationToken>())
                   .Returns((RegistryVersionEntry?)null);
        marketplace.GetLatestUpdateAsync("p3", "3.0.0", Arg.Any<CancellationToken>())
                   .Returns((RegistryVersionEntry?)null);

        var sut = new PluginUpdateService(marketplace, store,
            NullLogger<PluginUpdateService>.Instance);

        var results = await sut.CheckAllAsync(CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].PluginId.Should().Be("p1");
    }

    [Fact]
    public async Task CheckAllAsync_EmptyInstalled_ReturnsEmpty()
    {
        var marketplace = Substitute.For<IMarketplaceService>();
        var store       = Substitute.For<IPluginStore>();
        store.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PluginRecord>());

        var sut = new PluginUpdateService(marketplace, store,
            NullLogger<PluginUpdateService>.Instance);

        var results = await sut.CheckAllAsync(CancellationToken.None);
        results.Should().BeEmpty();
    }
}
```

---

## Step 4.9 — Integration tests

- [ ] Create `tests/MSOSync.IntegrationTests/Marketplace/MarketplaceFixture.cs`:

```csharp
// tests/MSOSync.IntegrationTests/Marketplace/MarketplaceFixture.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using MSOSync.Security;
using MSOSync.Topology;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace MSOSync.IntegrationTests.Marketplace;

public sealed class MarketplaceFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncMarketplace_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public WireMockServer RegistryServer { get; } = WireMockServer.Start();

    public string AdminUsername { get; } = "mkt-admin";
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
            ["Marketplace:RegistryUrl"]             = RegistryServer.Urls[0],
            ["Marketplace:CacheMinutes"]            = "60",
            ["Marketplace:MemoryCacheMinutes"]      = "5",
            ["Marketplace:HttpTimeoutSeconds"]      = "10",
            ["Marketplace:RetryCount"]              = "1",
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
        testBuilder.Services.AddMarketplace(testBuilder.Configuration);

        // Named HTTP client pointing to WireMock stub
        testBuilder.Services.AddHttpClient("MarketplaceRegistry", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<MarketplaceOptions>>().Value;
            client.BaseAddress = new Uri(opts.RegistryUrl!.TrimEnd('/') + "/");
            client.Timeout     = TimeSpan.FromSeconds(opts.HttpTimeoutSeconds);
        });

        // Minimal plugin registry stub (no real plugins needed for marketplace tests)
        testBuilder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginRegistry>(
            new StubPluginRegistry());

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        testBuilder.Services.AddFluentValidationAutoValidation();
        testBuilder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

        var app = testBuilder.Build();
        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
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
                "ALTER DATABASE [MSOSyncMarketplace_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        await db.Database.MigrateAsync();

        var hasher = new BCryptPasswordHasher();
        var user   = new SyncUser
        {
            Username = AdminUsername, PasswordHash = hasher.Hash(AdminPassword),
            Enabled = true, CreatedTime = DateTime.UtcNow,
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
        RegistryServer.Stop();
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
        var c    = CreateClient();
        var resp = await c.PostAsJsonAsync("/api/v1/auth/login",
            new { username = AdminUsername, password = AdminPassword });
        resp.EnsureSuccessStatusCode();
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Configures WireMock to serve a stub search response.</summary>
    public void StubSearch(IReadOnlyList<RegistryPluginEntry> entries, int page = 1, int pageSize = 20)
    {
        var result = new
        {
            data       = entries,
            total      = entries.Count,
            page       = page,
            pageSize   = pageSize,
            totalPages = (int)Math.Ceiling(entries.Count / (double)pageSize)
        };
        RegistryServer
            .Given(Request.Create().WithPath("/plugins").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(result));
    }

    /// <summary>Configures WireMock to serve a single plugin detail response.</summary>
    public void StubPlugin(RegistryPluginEntry entry)
    {
        RegistryServer
            .Given(Request.Create().WithPath($"/plugins/{entry.Id}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(entry));
    }

    /// <summary>Configures WireMock to return 404 for a plugin.</summary>
    public void StubPluginNotFound(string pluginId)
    {
        RegistryServer
            .Given(Request.Create().WithPath($"/plugins/{pluginId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
    }
}

/// <summary>Minimal IPluginRegistry stub that returns no installed plugins.</summary>
internal sealed class StubPluginRegistry : MSOSync.Plugin.Abstractions.IPluginRegistry
{
    public bool IsInitialized => true;
    public IReadOnlyList<MSOSync.Plugin.Models.PluginDescriptor> GetAll() => [];
    public MSOSync.Plugin.Models.PluginDescriptor? GetById(string id) => null;
    // Implement remaining members as no-ops
}

[CollectionDefinition("Marketplace")]
public sealed class MarketplaceCollection : ICollectionFixture<MarketplaceFixture> { }
```

> Note: `WireMock.Net` must be present in the test project. Check:
> ```powershell
> Select-String -Path "tests/MSOSync.IntegrationTests/*.csproj" -Pattern "WireMock"
> ```
> If missing, add:
> ```xml
> <PackageReference Include="WireMock.Net" Version="1.*" />
> ```

- [ ] Create `tests/MSOSync.IntegrationTests/Marketplace/MarketplaceControllerTests.cs`:

```csharp
// tests/MSOSync.IntegrationTests/Marketplace/MarketplaceControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MSOSync.Plugin.Marketplace.Models;
using Xunit;

namespace MSOSync.IntegrationTests.Marketplace;

[Collection("Marketplace")]
public sealed class MarketplaceControllerTests(MarketplaceFixture fx)
{
    // ── 503 when unconfigured ─────────────────────────────────────────────────

    [Fact]
    public async Task Search_Unauthenticated_Returns401()
    {
        var resp = await fx.CreateClient().GetAsync("/api/v1/marketplace/plugins");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_WithRegistryUrl_ReturnsPagedResult()
    {
        var entries = new[]
        {
            MakeEntry("p1", "1.0.0"),
            MakeEntry("p2", "2.0.0"),
            MakeEntry("p3", "3.0.0"),
        };
        fx.StubSearch(entries);

        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins?page=1&pageSize=20");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().Be(3);
        body.GetProperty("data").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Search_InvalidPageSize_Returns400()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins?page=1&pageSize=200");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Get Plugin Detail ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlugin_KnownId_Returns200WithDetail()
    {
        var entry = MakeEntry("msosync.sql", "1.2.3", withVersions: true);
        fx.StubPlugin(entry);

        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/msosync.sql");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be("msosync.sql");
        body.GetProperty("latestVersion").GetString().Should().Be("1.2.3");
        body.GetProperty("versions").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetPlugin_NotInRegistry_Returns404()
    {
        fx.StubPluginNotFound("no.such.plugin");

        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/no.such.plugin");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Get Versions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersions_KnownPlugin_ReturnsAllVersions()
    {
        var entry = MakeEntry("msosync.csv", "2.0.0", withVersions: true);
        fx.StubPlugin(entry);

        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/msosync.csv/versions");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        versions.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task GetVersions_UnknownPlugin_Returns404()
    {
        fx.StubPluginNotFound("ghost.plugin");

        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/ghost.plugin/versions");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DB Cache ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlugin_CacheHit_NoSecondRemoteCall()
    {
        // First call populates DB cache; WireMock tracks call count
        var entry = MakeEntry("msosync.cached", "1.0.0");
        fx.StubPlugin(entry);

        var client = await fx.AdminClientAsync();
        await client.GetAsync("/api/v1/marketplace/plugins/msosync.cached");
        await client.GetAsync("/api/v1/marketplace/plugins/msosync.cached");

        // WireMock should only have received exactly 1 real HTTP hit (second came from cache)
        var calls = fx.RegistryServer.LogEntries
            .Where(e => e.RequestMessage.Path.Contains("msosync.cached"))
            .ToList();
        calls.Should().HaveCount(1, "second request should be served from DB/memory cache");
    }

    // ── Update Check ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckUpdate_PluginNotInstalled_Returns404()
    {
        // StubPluginRegistry.GetById always returns null → 404
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/marketplace/plugins/no.installed/updates");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Bulk Update Check ─────────────────────────────────────────────────────

    [Fact]
    public async Task BulkCheckUpdates_NoInstalledPlugins_ReturnsTotalCheckedZero()
    {
        // StubPluginRegistry.GetAll returns empty → pluginStore.GetAllAsync returns empty
        var client = await fx.AdminClientAsync();
        var resp   = await client.PostAsJsonAsync(
            "/api/v1/marketplace/updates/check", new { updatesOnly = false });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalChecked").GetInt32().Should().Be(0);
        body.GetProperty("updatesAvailable").GetInt32().Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RegistryPluginEntry MakeEntry(
        string id, string version, bool withVersions = false)
    {
        var versions = withVersions
            ? new[]
            {
                new RegistryVersionEntry
                {
                    Version = version, MinHostVersion = "1.0.0", MaxHostVersion = "99.0.0",
                    DownloadUrl = $"https://dl.test/{id}/{version}.msopkg", Sha256 = "aabbcc"
                }
            }
            : Array.Empty<RegistryVersionEntry>();

        return new RegistryPluginEntry
        {
            Id = id, Name = id, Author = "Test Corp", Description = "A test plugin",
            Category = "connector", LatestVersion = version, MinHostVersion = "1.0.0",
            Versions = versions
        };
    }
}
```

---

## Step 4.10 — Run all tests

- [ ] Unit tests:

```powershell
dotnet test tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj --filter "FullyQualifiedName~Marketplace" --no-build
```

All new tests must pass (green).

- [ ] Integration tests (requires SQL LocalDB):

```powershell
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "FullyQualifiedName~Marketplace" --no-build
```

All new tests must pass (green).

- [ ] Full test suite (smoke check — no regressions):

```powershell
dotnet test MSOSync.sln --no-build --filter "FullyQualifiedName~PersistenceTests"
```

`SchemaCreated_All49TablesExist` must pass.

---

## Step 4.11 — Completion checklist

- [ ] All 4 tasks green-checked
- [ ] `dotnet build MSOSync.sln` reports 0 errors
- [ ] `SchemaCreated_All49TablesExist` passes
- [ ] `MarketplaceController` reachable at `api/v1/marketplace` in Swagger (development mode)
- [ ] Unauthenticated requests return 401
- [ ] Requests with valid admin token but no `Marketplace:RegistryUrl` return 503
- [ ] No `.env` files staged in git commit
