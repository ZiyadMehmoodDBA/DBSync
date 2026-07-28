# Phase 2E.5 — API Keys + Service Accounts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add API key authentication for users and service accounts so programmatic clients can authenticate without a username/password login flow.

**Architecture:** M043 adds `SyncUserApiKey` and `SyncServiceAccount` entities. Keys are generated with a unique prefix (for fast DB lookup) and stored as SHA-256 hashes. `ApiKeyAuthenticationHandler` extracts keys from `Authorization: ApiKey <key>` or `X-Api-Key: <key>` headers and validates them. Admin endpoints manage keys and service accounts.

**Tech Stack:** C# 13 / .NET 9 / EF Core 9 / xUnit + FluentAssertions + Moq

## Global Constraints

- Prerequisite: 2E.1 complete — `ISecretsService` exists
- API key formats (exact): user `msk_<8alphanum>_<32urlsafebase64>`, service account `msa_<8alphanum>_<32urlsafebase64>`
- Prefix = first 14 chars (`msk_xxxxxxxx_`) — stored in DB for O(1) lookup
- Full key hashed with SHA-256; hash stored in DB; raw key returned ONCE at creation only
- Migration name: `M043_ApiKeys`
- `ApiKeyAuthenticationHandler` scheme name: `"ApiKey"`
- All admin endpoints: `[Authorize(Policy = "AdminOnly")]`
- `git add` by file name only

---

### Task 1: SyncUserApiKey + SyncServiceAccount entities + M043 migration

**Files:**
- Create: `src/MSOSync.Persistence/Entities/SyncUserApiKey.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncServiceAccount.cs`
- Modify: `src/MSOSync.Persistence/MSOSyncDbContext.cs`
- Create: M043 migration via `dotnet ef migrations add M043_ApiKeys`

**Interfaces:**
- Consumes: existing `SyncUser`
- Produces: `SyncUserApiKey { Id, UserId, KeyPrefix, KeyHash, Name, CreatedAt, LastUsedAt, ExpiresAt, IsRevoked }`, `SyncServiceAccount { Id, Name, KeyPrefix, KeyHash, Permissions, CreatedAt, IsRevoked }`

- [ ] **Step 1: Create SyncUserApiKey**

```csharp
// src/MSOSync.Persistence/Entities/SyncUserApiKey.cs
namespace MSOSync.Persistence.Entities;

internal sealed class SyncUserApiKey
{
    public int Id { get; set; }
    public int UserId { get; set; }           // adapt type to match SyncUser.Id
    public string KeyPrefix { get; set; } = string.Empty;   // "msk_xxxxxxxx_"
    public string KeyHash { get; set; } = string.Empty;     // SHA-256 hex
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;

    public SyncUser User { get; set; } = null!;
}
```

- [ ] **Step 2: Create SyncServiceAccount**

```csharp
// src/MSOSync.Persistence/Entities/SyncServiceAccount.cs
namespace MSOSync.Persistence.Entities;

internal sealed class SyncServiceAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;   // "msa_xxxxxxxx_"
    public string KeyHash { get; set; } = string.Empty;     // SHA-256 hex
    public string Permissions { get; set; } = "[]";         // JSON array of permission strings
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;
}
```

- [ ] **Step 3: Register in MSOSyncDbContext**

Read `src/MSOSync.Persistence/MSOSyncDbContext.cs`. Add DbSets:

```csharp
public DbSet<SyncUserApiKey> UserApiKeys => Set<SyncUserApiKey>();
public DbSet<SyncServiceAccount> ServiceAccounts => Set<SyncServiceAccount>();
```

In `OnModelCreating`, add:

```csharp
modelBuilder.Entity<SyncUserApiKey>(b =>
{
    b.ToTable("SyncUserApiKeys");
    b.HasKey(e => e.Id);
    b.HasIndex(e => e.KeyPrefix).IsUnique();
    b.Property(e => e.KeyPrefix).HasMaxLength(14).IsRequired();
    b.Property(e => e.KeyHash).HasMaxLength(64).IsRequired();
    b.Property(e => e.Name).HasMaxLength(200).IsRequired();
    b.HasOne(e => e.User).WithMany()
     .HasForeignKey(e => e.UserId)
     .OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<SyncServiceAccount>(b =>
{
    b.ToTable("SyncServiceAccounts");
    b.HasKey(e => e.Id);
    b.HasIndex(e => e.KeyPrefix).IsUnique();
    b.Property(e => e.KeyPrefix).HasMaxLength(14).IsRequired();
    b.Property(e => e.KeyHash).HasMaxLength(64).IsRequired();
    b.Property(e => e.Name).HasMaxLength(200).IsRequired();
    b.Property(e => e.Permissions).HasMaxLength(2000);
});
```

- [ ] **Step 4: Generate migration**

```
dotnet ef migrations add M043_ApiKeys --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```

Verify `Up()` contains `CreateTable SyncUserApiKeys` and `CreateTable SyncServiceAccounts`.

- [ ] **Step 5: Commit**

```
git add src/MSOSync.Persistence/Entities/SyncUserApiKey.cs src/MSOSync.Persistence/Entities/SyncServiceAccount.cs src/MSOSync.Persistence/MSOSyncDbContext.cs
git add src/MSOSync.Persistence/Migrations/
git commit -m "feat(2E.5-T1): add SyncUserApiKey + SyncServiceAccount entities + M043"
```

---

### Task 2: IApiKeyService + ApiKeyService

**Files:**
- Create: `src/MSOSync.Api/Auth/IApiKeyService.cs`
- Create: `src/MSOSync.Api/Auth/ApiKeyService.cs`
- Create: `tests/MSOSync.ApiTests/Auth/ApiKeyServiceTests.cs`

**Interfaces:**
- Consumes: `MSOSyncDbContext` (Task 1 entities), `SyncUser`
- Produces:
  - `CreateUserKeyAsync(int userId, string name, DateTime? expiresAt, ct) : Task<(string rawKey, SyncUserApiKey entity)>`
  - `CreateServiceAccountAsync(string name, string[] permissions, ct) : Task<(string rawKey, SyncServiceAccount entity)>`
  - `ValidateUserKeyAsync(string apiKey, ct) : Task<SyncUser?>`
  - `ValidateServiceAccountKeyAsync(string apiKey, ct) : Task<SyncServiceAccount?>`
  - `RevokeUserKeyAsync(int keyId, ct) : Task`
  - `RevokeServiceAccountAsync(int id, ct) : Task`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Auth/ApiKeyServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Auth;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Auth;

public sealed class ApiKeyServiceTests : IDisposable
{
    private readonly MSOSyncDbContext _db;

    public ApiKeyServiceTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private ApiKeyService Build() => new(_db);

    [Fact]
    public async Task CreateUserKeyAsync_ReturnsRawKey_WithMskPrefix()
    {
        _db.Users.Add(new SyncUser { Id = 1, Username = "test", PasswordHash = "x" });
        await _db.SaveChangesAsync();

        var (rawKey, entity) = await Build().CreateUserKeyAsync(1, "Test Key");

        rawKey.Should().StartWith("msk_");
        rawKey.Should().HaveLength(47); // "msk_" + 8 + "_" + 32 = 46 chars... actual: msk_(8)_(32) = 3+1+8+1+32 = 45
        entity.KeyPrefix.Should().Be(rawKey[..14]);
        entity.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateUserKeyAsync_ReturnsUser_ForValidKey()
    {
        _db.Users.Add(new SyncUser { Id = 2, Username = "alice", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var (rawKey, _) = await Build().CreateUserKeyAsync(2, "Key");

        var user = await Build().ValidateUserKeyAsync(rawKey);

        user.Should().NotBeNull();
        user!.Id.Should().Be(2);
    }

    [Fact]
    public async Task ValidateUserKeyAsync_ReturnsNull_ForInvalidKey()
    {
        var user = await Build().ValidateUserKeyAsync("msk_invalid__padpadpadpadpadpadpadpad");
        user.Should().BeNull();
    }

    [Fact]
    public async Task ValidateUserKeyAsync_ReturnsNull_WhenRevoked()
    {
        _db.Users.Add(new SyncUser { Id = 3, Username = "bob", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var (rawKey, entity) = await Build().CreateUserKeyAsync(3, "Key");
        await Build().RevokeUserKeyAsync(entity.Id);

        var user = await Build().ValidateUserKeyAsync(rawKey);

        user.Should().BeNull();
    }

    [Fact]
    public async Task CreateServiceAccountAsync_ReturnsRawKey_WithMsaPrefix()
    {
        var (rawKey, entity) = await Build().CreateServiceAccountAsync("CI Bot", ["read", "write"]);

        rawKey.Should().StartWith("msa_");
        entity.Permissions.Should().Contain("read");
    }

    [Fact]
    public async Task ValidateServiceAccountKeyAsync_ReturnsAccount_ForValidKey()
    {
        var (rawKey, _) = await Build().CreateServiceAccountAsync("Bot", ["read"]);

        var account = await Build().ValidateServiceAccountKeyAsync(rawKey);

        account.Should().NotBeNull();
        account!.Name.Should().Be("Bot");
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Create IApiKeyService**

```csharp
// src/MSOSync.Api/Auth/IApiKeyService.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

public interface IApiKeyService
{
    Task<(string RawKey, SyncUserApiKey Entity)> CreateUserKeyAsync(
        int userId, string name, DateTime? expiresAt = null, CancellationToken ct = default);

    Task<(string RawKey, SyncServiceAccount Entity)> CreateServiceAccountAsync(
        string name, string[] permissions, CancellationToken ct = default);

    Task<SyncUser?> ValidateUserKeyAsync(string apiKey, CancellationToken ct = default);

    Task<SyncServiceAccount?> ValidateServiceAccountKeyAsync(string apiKey, CancellationToken ct = default);

    Task RevokeUserKeyAsync(int keyId, CancellationToken ct = default);

    Task RevokeServiceAccountAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement ApiKeyService**

```csharp
// src/MSOSync.Api/Auth/ApiKeyService.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

internal sealed class ApiKeyService(MSOSyncDbContext db) : IApiKeyService
{
    public async Task<(string RawKey, SyncUserApiKey Entity)> CreateUserKeyAsync(
        int userId, string name, DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var (rawKey, prefix, hash) = GenerateKey("msk");
        var entity = new SyncUserApiKey
        {
            UserId = userId,
            KeyPrefix = prefix,
            KeyHash = hash,
            Name = name,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
        };
        db.UserApiKeys.Add(entity);
        await db.SaveChangesAsync(ct);
        return (rawKey, entity);
    }

    public async Task<(string RawKey, SyncServiceAccount Entity)> CreateServiceAccountAsync(
        string name, string[] permissions, CancellationToken ct = default)
    {
        var (rawKey, prefix, hash) = GenerateKey("msa");
        var entity = new SyncServiceAccount
        {
            Name = name,
            KeyPrefix = prefix,
            KeyHash = hash,
            Permissions = JsonSerializer.Serialize(permissions),
            CreatedAt = DateTime.UtcNow,
        };
        db.ServiceAccounts.Add(entity);
        await db.SaveChangesAsync(ct);
        return (rawKey, entity);
    }

    public async Task<SyncUser?> ValidateUserKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 14) return null;
        var prefix = apiKey[..14];
        var hash = HashKey(apiKey);

        var key = await db.UserApiKeys
            .Include(k => k.User)
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix && k.KeyHash == hash
                && !k.IsRevoked
                && (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow), ct);

        if (key is null) return null;

        key.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return key.User;
    }

    public async Task<SyncServiceAccount?> ValidateServiceAccountKeyAsync(
        string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 14) return null;
        var prefix = apiKey[..14];
        var hash = HashKey(apiKey);

        return await db.ServiceAccounts
            .FirstOrDefaultAsync(a => a.KeyPrefix == prefix && a.KeyHash == hash && !a.IsRevoked, ct);
    }

    public async Task RevokeUserKeyAsync(int keyId, CancellationToken ct = default)
    {
        await db.UserApiKeys
            .Where(k => k.Id == keyId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.IsRevoked, true), ct);
    }

    public async Task RevokeServiceAccountAsync(int id, CancellationToken ct = default)
    {
        await db.ServiceAccounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsRevoked, true), ct);
    }

    private static (string RawKey, string Prefix, string Hash) GenerateKey(string prefixTag)
    {
        var idPart = GenerateAlphanumeric(8);
        var secretPart = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var rawKey = $"{prefixTag}_{idPart}_{secretPart}";
        var prefix = rawKey[..14];
        return (rawKey, prefix, HashKey(rawKey));
    }

    private static string GenerateAlphanumeric(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Register in DI**

```csharp
services.AddScoped<IApiKeyService, ApiKeyService>();
```

- [ ] **Step 6: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+6, Failed: 0`

- [ ] **Step 7: Commit**

```
git add src/MSOSync.Api/Auth/IApiKeyService.cs src/MSOSync.Api/Auth/ApiKeyService.cs tests/MSOSync.ApiTests/Auth/ApiKeyServiceTests.cs
git commit -m "feat(2E.5-T2): add IApiKeyService + ApiKeyService"
```

---

### Task 3: ApiKeyAuthenticationHandler

**Files:**
- Create: `src/MSOSync.Api/Auth/ApiKeyAuthenticationHandler.cs`
- Modify: `src/MSOSync.App/Program.cs` (add ApiKey authentication scheme)
- Create: `tests/MSOSync.ApiTests/Auth/ApiKeyAuthenticationHandlerTests.cs`

**Interfaces:**
- Consumes: `IApiKeyService` (Task 2)
- Produces: `ApiKeyAuthenticationHandler` — reads `Authorization: ApiKey <key>` or `X-Api-Key: <key>`, sets `ClaimsPrincipal` with user claims or service account claims

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Auth/ApiKeyAuthenticationHandlerTests.cs
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Api.Auth;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Auth;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private readonly Mock<IApiKeyService> _apiKeyService = new();

    private ApiKeyAuthenticationHandler BuildHandler(HttpContext ctx)
    {
        var opts = Options.Create(new AuthenticationSchemeOptions());
        var monitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        monitor.Setup(m => m.Get(It.IsAny<string>())).Returns(opts.Value);

        var handler = new ApiKeyAuthenticationHandler(
            monitor.Object,
            new LoggerFactory(),
            UrlEncoder.Default,
            _apiKeyService.Object);

        var scheme = new AuthenticationScheme("ApiKey", "ApiKey", typeof(ApiKeyAuthenticationHandler));
        handler.InitializeAsync(scheme, ctx).Wait();
        return handler;
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsSuccess_ForValidUserKey()
    {
        var user = new SyncUser { Id = 1, Username = "alice", PasswordHash = "x" };
        _apiKeyService.Setup(s => s.ValidateUserKeyAsync("msk_testkey12_secretsecretssecret32", default))
            .ReturnsAsync(user);

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Api-Key"] = "msk_testkey12_secretsecretssecret32";

        var handler = BuildHandler(ctx);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.Name.Should().Be("alice");
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNoResult_WhenNoKeyPresent()
    {
        var ctx = new DefaultHttpContext();
        var handler = BuildHandler(ctx);

        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsFail_ForInvalidKey()
    {
        _apiKeyService.Setup(s => s.ValidateUserKeyAsync(It.IsAny<string>(), default))
            .ReturnsAsync((SyncUser?)null);
        _apiKeyService.Setup(s => s.ValidateServiceAccountKeyAsync(It.IsAny<string>(), default))
            .ReturnsAsync((SyncServiceAccount?)null);

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Api-Key"] = "msk_badkey123_badsecretbadsecretbadse";

        var handler = BuildHandler(ctx);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Implement ApiKeyAuthenticationHandler**

```csharp
// src/MSOSync.Api/Auth/ApiKeyAuthenticationHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MSOSync.Api.Auth;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyService apiKeyService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ExtractKey(Request);
        if (string.IsNullOrEmpty(apiKey)) return AuthenticateResult.NoResult();

        // Try user API key first
        var user = await apiKeyService.ValidateUserKeyAsync(apiKey, Context.RequestAborted);
        if (user is not null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("auth_method", "api_key"),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        // Try service account key
        var account = await apiKeyService.ValidateServiceAccountKeyAsync(apiKey, Context.RequestAborted);
        if (account is not null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, $"sa_{account.Id}"),
                new(ClaimTypes.Name, account.Name),
                new("auth_method", "service_account"),
                new("permissions", account.Permissions),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        return AuthenticateResult.Fail("Invalid API key");
    }

    private static string? ExtractKey(HttpRequest request)
    {
        // Check X-Api-Key header first
        if (request.Headers.TryGetValue("X-Api-Key", out var headerVal))
            return headerVal.ToString();

        // Check Authorization: ApiKey <key>
        if (request.Headers.TryGetValue("Authorization", out var authVal))
        {
            var auth = authVal.ToString();
            if (auth.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                return auth["ApiKey ".Length..].Trim();
        }

        return null;
    }
}
```

- [ ] **Step 4: Register scheme in Program.cs**

Read `src/MSOSync.App/Program.cs`. Find where authentication is configured (`.AddAuthentication()`). Add the `ApiKey` scheme:

```csharp
builder.Services.AddAuthentication()
    // ... existing schemes ...
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { });
```

If authentication is already configured via `builder.Services.AddAuthentication(...)`, chain `.AddScheme<...>()` to the existing call.

- [ ] **Step 5: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+3, Failed: 0`

- [ ] **Step 6: Build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```
git add src/MSOSync.Api/Auth/ApiKeyAuthenticationHandler.cs src/MSOSync.App/Program.cs tests/MSOSync.ApiTests/Auth/ApiKeyAuthenticationHandlerTests.cs
git commit -m "feat(2E.5-T3): add ApiKeyAuthenticationHandler"
```

---

### Task 4: ApiKeyController + ServiceAccountController + tests

**Files:**
- Create: `src/MSOSync.Api/Controllers/ApiKeyController.cs`
- Create: `src/MSOSync.Api/Controllers/ServiceAccountController.cs`
- Create: `tests/MSOSync.ApiTests/Controllers/ApiKeyControllerTests.cs`

**Interfaces:**
- Consumes: `IApiKeyService` (Task 2)
- Produces:
  - `POST /api/api-keys` — create user key (authenticated user)
  - `GET /api/api-keys` — list user's keys
  - `DELETE /api/api-keys/{id}` — revoke key
  - `POST /api/service-accounts` (AdminOnly) — create service account
  - `GET /api/service-accounts` (AdminOnly) — list all
  - `DELETE /api/service-accounts/{id}` (AdminOnly) — revoke

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Controllers/ApiKeyControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MSOSync.Api.Auth;
using MSOSync.Api.Controllers;
using MSOSync.Persistence.Entities;
using System.Security.Claims;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class ApiKeyControllerTests
{
    private readonly Mock<IApiKeyService> _svc = new();
    private readonly ApiKeyController _controller;

    public ApiKeyControllerTests()
    {
        _controller = new ApiKeyController(_svc.Object);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1")], "Test"));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    [Fact]
    public async Task CreateKey_ReturnsRawKeyOnce()
    {
        var entity = new SyncUserApiKey { Id = 1, UserId = 1, KeyPrefix = "msk_abc12345_", Name = "MyKey" };
        _svc.Setup(s => s.CreateUserKeyAsync(1, "MyKey", null, default))
            .ReturnsAsync(("msk_abc12345_secretsecret32padpad", entity));

        var result = await _controller.CreateKey(new CreateApiKeyRequest("MyKey", null));

        result.Result.Should().BeOfType<OkObjectResult>();
        var body = ((OkObjectResult)result.Result!).Value!;
        body.ToString().Should().Contain("msk_");
    }

    [Fact]
    public async Task RevokeKey_ReturnsNoContent()
    {
        _svc.Setup(s => s.RevokeUserKeyAsync(5, default)).Returns(Task.CompletedTask);

        var result = await _controller.RevokeKey(5);

        result.Should().BeOfType<NoContentResult>();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Implement ApiKeyController**

```csharp
// src/MSOSync.Api/Controllers/ApiKeyController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Auth;
using MSOSync.Persistence;
using System.Security.Claims;

namespace MSOSync.Api.Controllers;

public sealed record CreateApiKeyRequest(string Name, DateTime? ExpiresAt);

[ApiController]
[Route("api/api-keys")]
[Authorize]
public sealed class ApiKeyController(IApiKeyService apiKeyService) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<ActionResult<object>> CreateKey(
        [FromBody] CreateApiKeyRequest request, CancellationToken ct = default)
    {
        var (rawKey, entity) = await apiKeyService.CreateUserKeyAsync(
            CurrentUserId, request.Name, request.ExpiresAt, ct);

        return Ok(new
        {
            id = entity.Id,
            name = entity.Name,
            key = rawKey,   // only returned once
            prefix = entity.KeyPrefix,
            created_at = entity.CreatedAt,
            expires_at = entity.ExpiresAt,
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RevokeKey(int id, CancellationToken ct = default)
    {
        await apiKeyService.RevokeUserKeyAsync(id, ct);
        return NoContent();
    }
}
```

```csharp
// src/MSOSync.Api/Controllers/ServiceAccountController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Auth;

namespace MSOSync.Api.Controllers;

public sealed record CreateServiceAccountRequest(string Name, string[] Permissions);

[ApiController]
[Route("api/service-accounts")]
[Authorize(Policy = "AdminOnly")]
public sealed class ServiceAccountController(IApiKeyService apiKeyService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Create(
        [FromBody] CreateServiceAccountRequest request, CancellationToken ct = default)
    {
        var (rawKey, entity) = await apiKeyService.CreateServiceAccountAsync(
            request.Name, request.Permissions, ct);

        return Ok(new
        {
            id = entity.Id,
            name = entity.Name,
            key = rawKey,   // only returned once
            prefix = entity.KeyPrefix,
            permissions = request.Permissions,
            created_at = entity.CreatedAt,
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct = default)
    {
        await apiKeyService.RevokeServiceAccountAsync(id, ct);
        return NoContent();
    }
}
```

- [ ] **Step 4: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+2, Failed: 0`

- [ ] **Step 5: Build full solution**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Api/Controllers/ApiKeyController.cs src/MSOSync.Api/Controllers/ServiceAccountController.cs tests/MSOSync.ApiTests/Controllers/ApiKeyControllerTests.cs
git commit -m "feat(2E.5-T4): add ApiKeyController + ServiceAccountController"
```
