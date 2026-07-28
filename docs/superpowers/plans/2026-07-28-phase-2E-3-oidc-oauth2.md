# Phase 2E.3 — OIDC/OAuth2 SSO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add OpenID Connect / OAuth2 SSO so users can authenticate via an external identity provider; local auth remains available as a fallback.

**Architecture:** `OidcConfiguration` entity stores provider settings in DB for admin management. `OidcAuthOptions` (from `appsettings.json`/secrets) configures the live OIDC middleware. `OidcUserProvisioningService` finds-or-creates a `SyncUser` from incoming claims. On successful callback the middleware provisions the user and issues the app's standard JWT so callers see a uniform token. Admin REST endpoints allow CRUD on persisted `OidcConfiguration` records.

**Tech Stack:** C# 13 / .NET 9 / Microsoft.AspNetCore.Authentication.OpenIdConnect 9.0.0 / EF Core 9 / xUnit + FluentAssertions + Moq

## Global Constraints

- Prerequisite: 2E.1 complete — `ISecretsService`, `CompositeSecretsService`, `SecretsServiceExtensions` exist in `MSOSync.Secrets`
- Migration name: `M041_OidcConfiguration` — run via `dotnet ef migrations add`
- All admin endpoints: `[Authorize(Policy = "AdminOnly")]`
- OIDC flow: `/auth/oidc/login` → provider → `/auth/oidc/callback` (middleware) → provision user → issue JWT → redirect `{FrontendCallbackUrl}?token=<jwt>`
- OIDC users bypass local MFA in 2E.3 (MFA added in 2E.4)
- Client secret fetched from `ISecretsService` at request time using key stored in `OidcAuthOptions.ClientSecretKey`
- `git add` by file name only

---

### Task 1: OidcConfiguration entity + M041 migration

**Files:**
- Create: `src/MSOSync.Persistence/Entities/OidcConfiguration.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncUser.cs` (add ExternalId, AuthProvider, Email)
- Modify: `src/MSOSync.Persistence/MSOSyncDbContext.cs` (add DbSet + model config)
- Create: M041 migration via `dotnet ef migrations add M041_OidcConfiguration`

**Interfaces:**
- Consumes: existing `SyncUser`, existing `MSOSyncDbContext`
- Produces: `OidcConfiguration { Id, Name, Authority, ClientId, ClientSecretKey, Scopes, CallbackPath, IsEnabled, CreatedAt }`, `SyncUser.ExternalId`, `SyncUser.AuthProvider`, `SyncUser.Email`

- [ ] **Step 1: Create OidcConfiguration entity**

```csharp
// src/MSOSync.Persistence/Entities/OidcConfiguration.cs
namespace MSOSync.Persistence.Entities;

internal sealed class OidcConfiguration
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretKey { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid profile email";
    public string CallbackPath { get; set; } = "/auth/oidc/callback";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Add nullable columns to SyncUser**

Read `src/MSOSync.Persistence/Entities/SyncUser.cs`. Append after existing properties:

```csharp
public string? ExternalId { get; set; }    // OIDC subject claim
public string? AuthProvider { get; set; }  // "local" | "oidc:<providerName>"
public string? Email { get; set; }
```

- [ ] **Step 3: Register in MSOSyncDbContext**

Read `src/MSOSync.Persistence/MSOSyncDbContext.cs`. Add DbSet:

```csharp
public DbSet<OidcConfiguration> OidcConfigurations => Set<OidcConfiguration>();
```

In `OnModelCreating`, add:

```csharp
modelBuilder.Entity<OidcConfiguration>(b =>
{
    b.ToTable("OidcConfigurations");
    b.HasKey(e => e.Id);
    b.Property(e => e.Name).HasMaxLength(200).IsRequired();
    b.Property(e => e.Authority).HasMaxLength(500).IsRequired();
    b.Property(e => e.ClientId).HasMaxLength(200).IsRequired();
    b.Property(e => e.ClientSecretKey).HasMaxLength(500).IsRequired();
    b.Property(e => e.Scopes).HasMaxLength(500);
    b.Property(e => e.CallbackPath).HasMaxLength(200);
});
```

For the SyncUser entity configuration block (find it in `OnModelCreating`), add if not already mapped:

```csharp
b.Property(e => e.ExternalId).HasMaxLength(500);
b.Property(e => e.AuthProvider).HasMaxLength(100);
b.Property(e => e.Email).HasMaxLength(320);
```

- [ ] **Step 4: Generate migration**

```
dotnet ef migrations add M041_OidcConfiguration --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```

Verify the generated `Up()` contains:
- `AddColumn` for `ExternalId` (nvarchar(500), nullable) on SyncUsers
- `AddColumn` for `AuthProvider` (nvarchar(100), nullable) on SyncUsers
- `AddColumn` for `Email` (nvarchar(320), nullable) on SyncUsers
- `CreateTable` for `OidcConfigurations`

- [ ] **Step 5: Commit**

Run `git status` to get exact migration file names, then:

```
git add src/MSOSync.Persistence/Entities/OidcConfiguration.cs src/MSOSync.Persistence/Entities/SyncUser.cs src/MSOSync.Persistence/MSOSyncDbContext.cs
git add src/MSOSync.Persistence/Migrations/
git commit -m "feat(2E.3-T1): add OidcConfiguration entity + M041 migration"
```

Note: use `git add src/MSOSync.Persistence/Migrations/<timestamp>_M041_OidcConfiguration.cs` and the updated snapshot file by exact name.

---

### Task 2: OIDC authentication middleware + DI extension

**Files:**
- Create: `src/MSOSync.Api/Auth/OidcAuthOptions.cs`
- Create: `src/MSOSync.Api/Auth/OidcAuthenticationExtensions.cs`
- Modify: `src/MSOSync.Api/MSOSync.Api.csproj` (add OpenIdConnect package + Secrets reference)
- Modify: `src/MSOSync.App/Program.cs` (register OIDC)
- Modify: `src/MSOSync.App/appsettings.json` (add Oidc section)

**Interfaces:**
- Consumes: `ISecretsService` (2E.1), `IOidcUserProvisioningService` (Task 3 — forward reference, register after Task 3 is implemented)
- Produces: `AddOidcAuthentication(IServiceCollection, IConfiguration)` extension; `/auth/oidc/login` challenge; `/auth/oidc/callback` handled by middleware

- [ ] **Step 1: Add packages and project reference**

In `src/MSOSync.Api/MSOSync.Api.csproj`, add inside an `<ItemGroup>`:

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.Cookies" Version="9.0.0" />
<ProjectReference Include="..\MSOSync.Secrets\MSOSync.Secrets.csproj" />
```

- [ ] **Step 2: Create OidcAuthOptions**

```csharp
// src/MSOSync.Api/Auth/OidcAuthOptions.cs
namespace MSOSync.Api.Auth;

public sealed class OidcAuthOptions
{
    public const string Section = "Oidc";

    public bool Enabled { get; set; } = false;
    public string ProviderName { get; set; } = "oidc";
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretKey { get; set; } = "Oidc:ClientSecret";
    public string Scopes { get; set; } = "openid profile email";
    public string FrontendCallbackUrl { get; set; } = "/auth/sso-callback";
}
```

- [ ] **Step 3: Add Oidc section to appsettings.json**

Read `src/MSOSync.App/appsettings.json`. Add:

```json
"Oidc": {
  "Enabled": false,
  "ProviderName": "oidc",
  "Authority": "",
  "ClientId": "",
  "ClientSecretKey": "Oidc:ClientSecret",
  "Scopes": "openid profile email",
  "FrontendCallbackUrl": "/auth/sso-callback"
}
```

- [ ] **Step 4: Find the existing JWT token generation service**

Read the files in `src/MSOSync.Api/Services/` (or wherever auth/JWT services live). Find the interface that has a method to generate a JWT token from a `SyncUser`. It likely has a signature like:

```csharp
string CreateToken(SyncUser user);
// or:
string GenerateToken(SyncUser user);
```

Note the exact interface name (e.g., `IJwtService`, `ITokenService`, `IJwtTokenService`) and method name — replace `IJwtService.CreateToken` everywhere in Step 5 below with the actual names found.

- [ ] **Step 5: Create OidcAuthenticationExtensions**

```csharp
// src/MSOSync.Api/Auth/OidcAuthenticationExtensions.cs
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MSOSync.Secrets;

namespace MSOSync.Api.Auth;

public static class OidcAuthenticationExtensions
{
    public static IServiceCollection AddOidcAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OidcAuthOptions>()
            .BindConfiguration(OidcAuthOptions.Section)
            .ValidateOnStart();

        var opts = configuration.GetSection(OidcAuthOptions.Section).Get<OidcAuthOptions>() ?? new();
        if (!opts.Enabled) return services;

        services.AddAuthentication(o =>
        {
            o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, oidcOpts =>
        {
            oidcOpts.Authority = opts.Authority;
            oidcOpts.ClientId = opts.ClientId;
            oidcOpts.ResponseType = "code";
            oidcOpts.SaveTokens = false;
            oidcOpts.GetClaimsFromUserInfoEndpoint = true;
            oidcOpts.CallbackPath = "/auth/oidc/callback";

            foreach (var scope in opts.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                oidcOpts.Scope.Add(scope);

            oidcOpts.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = async ctx =>
                {
                    var secrets = ctx.HttpContext.RequestServices.GetRequiredService<ISecretsService>();
                    ctx.ProtocolMessage.ClientSecret =
                        await secrets.GetSecretAsync(opts.ClientSecretKey) ?? string.Empty;
                },

                OnTokenValidated = async ctx =>
                {
                    var provisioning = ctx.HttpContext.RequestServices
                        .GetRequiredService<IOidcUserProvisioningService>();
                    var user = await provisioning.ProvisionAsync(
                        ctx.Principal!, opts.ProviderName, ctx.HttpContext.RequestAborted);

                    // Replace IJwtService + CreateToken with the actual interface/method found in Step 4
                    var jwtService = ctx.HttpContext.RequestServices.GetRequiredService<IJwtService>();
                    var token = jwtService.CreateToken(user);

                    ctx.Response.Redirect(
                        $"{opts.FrontendCallbackUrl}?token={Uri.EscapeDataString(token)}");
                    ctx.HandleResponse();
                }
            };
        });

        return services;
    }
}
```

- [ ] **Step 6: Register in Program.cs**

Read `src/MSOSync.App/Program.cs`. Add after existing service registrations (before `var app = builder.Build()`):

```csharp
builder.Services.AddOidcAuthentication(builder.Configuration);
```

Add `using MSOSync.Api.Auth;` at the top if needed.

- [ ] **Step 7: Build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.` (If `IOidcUserProvisioningService` reference fails, add a stub interface temporarily; it will be replaced in Task 3.)

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Api/Auth/OidcAuthOptions.cs src/MSOSync.Api/Auth/OidcAuthenticationExtensions.cs src/MSOSync.Api/MSOSync.Api.csproj src/MSOSync.App/Program.cs src/MSOSync.App/appsettings.json
git commit -m "feat(2E.3-T2): add OIDC middleware + DI extension"
```

---

### Task 3: OidcUserProvisioningService + claim mapping

**Files:**
- Create: `src/MSOSync.Api/Auth/IOidcUserProvisioningService.cs`
- Create: `src/MSOSync.Api/Auth/OidcUserProvisioningService.cs`
- Create: `tests/MSOSync.ApiTests/Auth/OidcUserProvisioningServiceTests.cs`
- Modify: `src/MSOSync.Api/Auth/OidcAuthenticationExtensions.cs` (register the service in DI)

**Interfaces:**
- Consumes: `MSOSyncDbContext`, `SyncUser` with new columns (Task 1)
- Produces: `IOidcUserProvisioningService.ProvisionAsync(ClaimsPrincipal, string providerName, CancellationToken) : Task<SyncUser>`

- [ ] **Step 1: Write failing tests**

Note: read `src/MSOSync.Persistence/MSOSyncDbContext.cs` to verify the DbSet name for SyncUser (likely `Users` or `SyncUsers`) and replace `db.Users` below with the actual name.

```csharp
// tests/MSOSync.ApiTests/Auth/OidcUserProvisioningServiceTests.cs
using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Auth;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Auth;

public sealed class OidcUserProvisioningServiceTests : IDisposable
{
    private readonly MSOSyncDbContext _db;

    public OidcUserProvisioningServiceTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static ClaimsPrincipal MakePrincipal(string sub, string email, string name) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim("sub", sub),
            new Claim("email", email),
            new Claim("name", name),
        }, "oidc"));

    [Fact]
    public async Task ProvisionAsync_CreatesUser_WhenNotExists()
    {
        var svc = new OidcUserProvisioningService(_db);
        var principal = MakePrincipal("sub-123", "user@example.com", "Test User");

        var user = await svc.ProvisionAsync(principal, "azure");

        user.ExternalId.Should().Be("sub-123");
        user.Email.Should().Be("user@example.com");
        user.AuthProvider.Should().Be("oidc:azure");
        _db.Users.Count(u => u.ExternalId == "sub-123").Should().Be(1);
    }

    [Fact]
    public async Task ProvisionAsync_ReturnsExistingUser_WhenAlreadyProvisioned()
    {
        _db.Users.Add(new SyncUser
        {
            ExternalId = "sub-456",
            AuthProvider = "oidc:google",
            Email = "existing@example.com",
            Username = "existing@example.com",
            PasswordHash = string.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new OidcUserProvisioningService(_db);
        var principal = MakePrincipal("sub-456", "existing@example.com", "Existing");

        var user = await svc.ProvisionAsync(principal, "google");

        user.ExternalId.Should().Be("sub-456");
        _db.Users.Count().Should().Be(1);
    }

    [Fact]
    public async Task ProvisionAsync_Throws_WhenSubClaimMissing()
    {
        var svc = new OidcUserProvisioningService(_db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "oidc"));

        var act = () => svc.ProvisionAsync(principal, "test");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sub*");
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Create interface**

```csharp
// src/MSOSync.Api/Auth/IOidcUserProvisioningService.cs
using System.Security.Claims;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

public interface IOidcUserProvisioningService
{
    Task<SyncUser> ProvisionAsync(
        ClaimsPrincipal principal,
        string providerName,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement OidcUserProvisioningService**

Replace `db.Users` with the actual DbSet name from `MSOSyncDbContext`. Replace property names (Username, PasswordHash) with actual `SyncUser` property names.

```csharp
// src/MSOSync.Api/Auth/OidcUserProvisioningService.cs
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

internal sealed class OidcUserProvisioningService(MSOSyncDbContext db) : IOidcUserProvisioningService
{
    public async Task<SyncUser> ProvisionAsync(
        ClaimsPrincipal principal,
        string providerName,
        CancellationToken ct = default)
    {
        var sub = principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("OIDC principal missing 'sub' claim");

        var authProvider = $"oidc:{providerName}";

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.ExternalId == sub && u.AuthProvider == authProvider, ct);
        if (existing is not null) return existing;

        var email = principal.FindFirstValue("email") ?? sub;
        var user = new SyncUser
        {
            ExternalId = sub,
            AuthProvider = authProvider,
            Email = email,
            Username = email,
            PasswordHash = string.Empty,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
```

- [ ] **Step 5: Register service in OidcAuthenticationExtensions**

In `src/MSOSync.Api/Auth/OidcAuthenticationExtensions.cs`, inside `AddOidcAuthentication` before the `.AddAuthentication()` call:

```csharp
services.AddScoped<IOidcUserProvisioningService, OidcUserProvisioningService>();
```

- [ ] **Step 6: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+3, Failed: 0`

- [ ] **Step 7: Commit**

```
git add src/MSOSync.Api/Auth/IOidcUserProvisioningService.cs src/MSOSync.Api/Auth/OidcUserProvisioningService.cs src/MSOSync.Api/Auth/OidcAuthenticationExtensions.cs tests/MSOSync.ApiTests/Auth/OidcUserProvisioningServiceTests.cs
git commit -m "feat(2E.3-T3): add OidcUserProvisioningService with claim mapping"
```

---

### Task 4: OidcController (admin CRUD + login endpoint) + tests

**Files:**
- Create: `src/MSOSync.Api/Controllers/OidcController.cs`
- Create: `tests/MSOSync.ApiTests/Controllers/OidcControllerTests.cs`

**Interfaces:**
- Consumes: `MSOSyncDbContext`, `OidcConfiguration` (Task 1)
- Produces:
  - `GET /api/oidc/configurations` (AdminOnly)
  - `POST /api/oidc/configurations` (AdminOnly)
  - `PUT /api/oidc/configurations/{id}` (AdminOnly)
  - `DELETE /api/oidc/configurations/{id}` (AdminOnly)
  - `GET /auth/oidc/login` (AllowAnonymous — triggers OIDC challenge)

- [ ] **Step 1: Write failing tests**

Read `src/MSOSync.Persistence/MSOSyncDbContext.cs` to verify the DbSet name used in tests below (`OidcConfigurations`).

```csharp
// tests/MSOSync.ApiTests/Controllers/OidcControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Controllers;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class OidcControllerTests : IDisposable
{
    private readonly MSOSyncDbContext _db;
    private readonly OidcController _controller;

    public OidcControllerTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
        _controller = new OidcController(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetConfigurations_ReturnsAll()
    {
        _db.OidcConfigurations.Add(new OidcConfiguration
        {
            Name = "Azure AD",
            Authority = "https://login.microsoftonline.com/tenant",
            ClientId = "client-1",
            ClientSecretKey = "Oidc:ClientSecret",
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetConfigurations();

        result.Value.Should().HaveCount(1);
        result.Value!.First().Name.Should().Be("Azure AD");
    }

    [Fact]
    public async Task CreateConfiguration_AddsToDb()
    {
        var dto = new OidcConfigurationDto(
            "Google", "https://accounts.google.com", "google-client", "Oidc:ClientSecret:Google");

        var result = await _controller.CreateConfiguration(dto);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        _db.OidcConfigurations.Should().ContainSingle(c => c.Name == "Google");
    }

    [Fact]
    public async Task UpdateConfiguration_ModifiesEntity()
    {
        var config = new OidcConfiguration
        {
            Name = "Old Name",
            Authority = "https://old.example.com",
            ClientId = "old-client",
            ClientSecretKey = "old-key",
        };
        _db.OidcConfigurations.Add(config);
        await _db.SaveChangesAsync();

        var dto = new OidcConfigurationDto("New Name", "https://new.example.com", "new-client", "new-key");
        var result = await _controller.UpdateConfiguration(config.Id, dto);

        result.Should().BeOfType<NoContentResult>();
        var updated = await _db.OidcConfigurations.FindAsync(config.Id);
        updated!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task DeleteConfiguration_RemovesFromDb()
    {
        var config = new OidcConfiguration
        {
            Name = "ToDelete",
            Authority = "https://auth.example.com",
            ClientId = "c1",
            ClientSecretKey = "k1",
        };
        _db.OidcConfigurations.Add(config);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteConfiguration(config.Id);

        result.Should().BeOfType<NoContentResult>();
        _db.OidcConfigurations.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteConfiguration_ReturnsNotFound_WhenMissing()
    {
        var result = await _controller.DeleteConfiguration(999);
        result.Should().BeOfType<NotFoundResult>();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Implement OidcController**

```csharp
// src/MSOSync.Api/Controllers/OidcController.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Controllers;

public sealed record OidcConfigurationDto(
    string Name,
    string Authority,
    string ClientId,
    string ClientSecretKey,
    string Scopes = "openid profile email",
    string CallbackPath = "/auth/oidc/callback",
    bool IsEnabled = true);

[ApiController]
public sealed class OidcController(MSOSyncDbContext db) : ControllerBase
{
    [HttpGet("api/oidc/configurations")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IEnumerable<OidcConfigurationDto>>> GetConfigurations()
    {
        var configs = await db.OidcConfigurations
            .Select(c => new OidcConfigurationDto(
                c.Name, c.Authority, c.ClientId, c.ClientSecretKey,
                c.Scopes, c.CallbackPath, c.IsEnabled))
            .ToListAsync();
        return Ok(configs);
    }

    [HttpPost("api/oidc/configurations")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<OidcConfigurationDto>> CreateConfiguration(OidcConfigurationDto dto)
    {
        var entity = new OidcConfiguration
        {
            Name = dto.Name,
            Authority = dto.Authority,
            ClientId = dto.ClientId,
            ClientSecretKey = dto.ClientSecretKey,
            Scopes = dto.Scopes,
            CallbackPath = dto.CallbackPath,
            IsEnabled = dto.IsEnabled,
            CreatedAt = DateTime.UtcNow,
        };
        db.OidcConfigurations.Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetConfigurations), null, dto);
    }

    [HttpPut("api/oidc/configurations/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateConfiguration(int id, OidcConfigurationDto dto)
    {
        var entity = await db.OidcConfigurations.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Name = dto.Name;
        entity.Authority = dto.Authority;
        entity.ClientId = dto.ClientId;
        entity.ClientSecretKey = dto.ClientSecretKey;
        entity.Scopes = dto.Scopes;
        entity.CallbackPath = dto.CallbackPath;
        entity.IsEnabled = dto.IsEnabled;

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("api/oidc/configurations/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteConfiguration(int id)
    {
        var entity = await db.OidcConfigurations.FindAsync(id);
        if (entity is null) return NotFound();

        db.OidcConfigurations.Remove(entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("/auth/oidc/login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl = null)
        => Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
            OpenIdConnectDefaults.AuthenticationScheme);
}
```

- [ ] **Step 4: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+5, Failed: 0`

- [ ] **Step 5: Build full solution**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Api/Controllers/OidcController.cs tests/MSOSync.ApiTests/Controllers/OidcControllerTests.cs
git commit -m "feat(2E.3-T4): add OidcController with admin CRUD + login endpoint"
```
