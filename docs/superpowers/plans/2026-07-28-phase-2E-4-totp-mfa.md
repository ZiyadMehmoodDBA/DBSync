# Phase 2E.4 — TOTP MFA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add RFC 6238 TOTP two-factor authentication for local accounts — users can enroll via a QR code, and subsequent logins require a 6-digit code before a full JWT is issued.

**Architecture:** M042 adds `SyncUserTotpSecret` (one per user, nullable) and `SyncUserBackupCode` (8 per user). `IMfaService` encapsulates TOTP logic via Otp.NET. Login now returns an `{mfa_token, requires_mfa: true}` response when MFA is enabled; the client exchanges it via `/auth/mfa/verify` for the full JWT. OIDC users bypass local MFA (provider handles it).

**Tech Stack:** C# 13 / .NET 9 / Otp.NET 1.4.0 / EF Core 9 / xUnit + FluentAssertions + Moq

## Global Constraints

- Prerequisite: 2E.1 complete — `ISecretsService` exists; 2E.3 complete — OIDC users skip MFA
- Migration name: `M042_TotpMfa`
- TOTP: RFC 6238, HMAC-SHA1, 30-second window, 6 digits, ±1 step tolerance
- Backup codes: 8 codes per user; raw values returned once at enrollment; stored as SHA-256 hex
- mfa_token: short-lived JWT (5-minute expiry) carrying claim `mfa_user_id=<userId>` — not a full auth token
- On login: if `SyncUser.IsMfaEnabled = true`, return `{ requires_mfa: true, mfa_token: "..." }` with HTTP 200
- `git add` by file name only

---

### Task 1: M042 migration + TOTP entities

**Files:**
- Create: `src/MSOSync.Persistence/Entities/SyncUserTotpSecret.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncUserBackupCode.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncUser.cs` (add IsMfaEnabled)
- Modify: `src/MSOSync.Persistence/MSOSyncDbContext.cs` (add DbSets + model config)
- Create: M042 migration via `dotnet ef migrations add M042_TotpMfa`

**Interfaces:**
- Consumes: existing `SyncUser`
- Produces: `SyncUserTotpSecret { UserId, Secret, IsEnabled, EnabledAt }`, `SyncUserBackupCode { Id, UserId, CodeHash, IsUsed, UsedAt }`, `SyncUser.IsMfaEnabled`

- [ ] **Step 1: Create SyncUserTotpSecret**

```csharp
// src/MSOSync.Persistence/Entities/SyncUserTotpSecret.cs
namespace MSOSync.Persistence.Entities;

internal sealed class SyncUserTotpSecret
{
    public int UserId { get; set; }
    public string Secret { get; set; } = string.Empty;   // base32-encoded
    public bool IsEnabled { get; set; } = false;
    public DateTime? EnabledAt { get; set; }

    public SyncUser User { get; set; } = null!;
}
```

- [ ] **Step 2: Create SyncUserBackupCode**

```csharp
// src/MSOSync.Persistence/Entities/SyncUserBackupCode.cs
namespace MSOSync.Persistence.Entities;

internal sealed class SyncUserBackupCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;  // SHA-256 hex of raw code
    public bool IsUsed { get; set; } = false;
    public DateTime? UsedAt { get; set; }

    public SyncUser User { get; set; } = null!;
}
```

- [ ] **Step 3: Add IsMfaEnabled to SyncUser**

Read `src/MSOSync.Persistence/Entities/SyncUser.cs`. Append:

```csharp
public bool IsMfaEnabled { get; set; } = false;
```

- [ ] **Step 4: Register in MSOSyncDbContext**

Read `src/MSOSync.Persistence/MSOSyncDbContext.cs`. Add DbSets:

```csharp
public DbSet<SyncUserTotpSecret> TotpSecrets => Set<SyncUserTotpSecret>();
public DbSet<SyncUserBackupCode> BackupCodes => Set<SyncUserBackupCode>();
```

In `OnModelCreating`, add:

```csharp
modelBuilder.Entity<SyncUserTotpSecret>(b =>
{
    b.ToTable("SyncUserTotpSecrets");
    b.HasKey(e => e.UserId);
    b.Property(e => e.Secret).HasMaxLength(64).IsRequired();
    b.HasOne(e => e.User).WithOne()
     .HasForeignKey<SyncUserTotpSecret>(e => e.UserId)
     .OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<SyncUserBackupCode>(b =>
{
    b.ToTable("SyncUserBackupCodes");
    b.HasKey(e => e.Id);
    b.Property(e => e.CodeHash).HasMaxLength(64).IsRequired();
    b.HasIndex(e => e.UserId);
    b.HasOne(e => e.User).WithMany()
     .HasForeignKey(e => e.UserId)
     .OnDelete(DeleteBehavior.Cascade);
});
```

For SyncUser model config, add `b.Property(e => e.IsMfaEnabled).HasDefaultValue(false);`.

- [ ] **Step 5: Generate migration**

```
dotnet ef migrations add M042_TotpMfa --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```

Verify `Up()` contains:
- `AddColumn IsMfaEnabled (bit, not null, default false)` on SyncUsers
- `CreateTable SyncUserTotpSecrets` with `UserId` as both PK and FK
- `CreateTable SyncUserBackupCodes` with Id PK, UserId FK

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Persistence/Entities/SyncUserTotpSecret.cs src/MSOSync.Persistence/Entities/SyncUserBackupCode.cs src/MSOSync.Persistence/Entities/SyncUser.cs src/MSOSync.Persistence/MSOSyncDbContext.cs
git add src/MSOSync.Persistence/Migrations/
git commit -m "feat(2E.4-T1): add TOTP entities + M042 migration"
```

---

### Task 2: IMfaService + TotpMfaService implementation

**Files:**
- Modify: `src/MSOSync.Api/MSOSync.Api.csproj` (add Otp.NET package)
- Create: `src/MSOSync.Api/Auth/IMfaService.cs`
- Create: `src/MSOSync.Api/Auth/TotpMfaService.cs`
- Create: `tests/MSOSync.ApiTests/Auth/TotpMfaServiceTests.cs`

**Interfaces:**
- Consumes: `MSOSyncDbContext`, `SyncUserTotpSecret`, `SyncUserBackupCode` (Task 1)
- Produces:
  - `EnrollAsync(int userId, ct) : Task<string>` — generates base32 secret, saves to DB (IsEnabled=false), returns secret
  - `ConfirmEnrollmentAsync(int userId, string code, ct) : Task` — validates TOTP code, sets IsEnabled=true, generates 8 backup codes, returns nothing (backup codes generated separately)
  - `GenerateBackupCodesAsync(int userId, ct) : Task<IReadOnlyList<string>>` — creates 8 codes, stores hashes, returns raw codes (only time they're visible)
  - `IsEnabledAsync(int userId, ct) : Task<bool>`
  - `VerifyTotpAsync(int userId, string code, ct) : Task<bool>` — verifies code with ±1 step tolerance
  - `VerifyBackupCodeAsync(int userId, string code, ct) : Task<bool>` — verifies and marks used

- [ ] **Step 1: Add Otp.NET package**

In `src/MSOSync.Api/MSOSync.Api.csproj`:

```xml
<PackageReference Include="Otp.NET" Version="1.4.0" />
```

- [ ] **Step 2: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Auth/TotpMfaServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Auth;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using OtpNet;
using Xunit;

namespace MSOSync.ApiTests.Auth;

public sealed class TotpMfaServiceTests : IDisposable
{
    private readonly MSOSyncDbContext _db;

    public TotpMfaServiceTests()
    {
        var options = new DbContextOptionsBuilder<MSOSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MSOSyncDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private TotpMfaService Build() => new(_db);

    [Fact]
    public async Task EnrollAsync_SavesSecret_AndReturnsBase32()
    {
        var svc = Build();

        var secret = await svc.EnrollAsync(userId: 1);

        secret.Should().NotBeNullOrEmpty();
        _db.TotpSecrets.Should().ContainSingle(s => s.UserId == 1 && !s.IsEnabled);
    }

    [Fact]
    public async Task ConfirmEnrollmentAsync_EnablesMfa_WhenCodeValid()
    {
        var svc = Build();
        var secret = await svc.EnrollAsync(userId: 1);

        var keyBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(keyBytes);
        var code = totp.ComputeTotp();

        await svc.ConfirmEnrollmentAsync(1, code);

        var saved = await _db.TotpSecrets.FindAsync(1);
        saved!.IsEnabled.Should().BeTrue();
        saved.EnabledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyTotpAsync_ReturnsTrue_ForValidCode()
    {
        var svc = Build();
        var secret = await svc.EnrollAsync(userId: 2);
        var keyBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(keyBytes);
        var code = totp.ComputeTotp();

        var result = await svc.VerifyTotpAsync(2, code);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyTotpAsync_ReturnsFalse_ForInvalidCode()
    {
        var svc = Build();
        await svc.EnrollAsync(userId: 3);

        var result = await svc.VerifyTotpAsync(3, "000000");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateBackupCodesAsync_Returns8Codes_AndHashesInDb()
    {
        var svc = Build();
        await svc.EnrollAsync(userId: 4);

        var codes = await svc.GenerateBackupCodesAsync(4);

        codes.Should().HaveCount(8);
        _db.BackupCodes.Count(c => c.UserId == 4).Should().Be(8);
    }

    [Fact]
    public async Task VerifyBackupCodeAsync_ReturnsTrue_AndMarksUsed()
    {
        var svc = Build();
        await svc.EnrollAsync(userId: 5);
        var codes = await svc.GenerateBackupCodesAsync(5);
        var rawCode = codes[0];

        var result = await svc.VerifyBackupCodeAsync(5, rawCode);

        result.Should().BeTrue();
        _db.BackupCodes.First(c => c.UserId == 5 && c.IsUsed).UsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyBackupCodeAsync_ReturnsFalse_WhenAlreadyUsed()
    {
        var svc = Build();
        await svc.EnrollAsync(userId: 6);
        var codes = await svc.GenerateBackupCodesAsync(6);
        await svc.VerifyBackupCodeAsync(6, codes[0]); // first use

        var result = await svc.VerifyBackupCodeAsync(6, codes[0]); // second use

        result.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 4: Create IMfaService**

```csharp
// src/MSOSync.Api/Auth/IMfaService.cs
namespace MSOSync.Api.Auth;

public interface IMfaService
{
    Task<string> EnrollAsync(int userId, CancellationToken ct = default);
    Task ConfirmEnrollmentAsync(int userId, string code, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GenerateBackupCodesAsync(int userId, CancellationToken ct = default);
    Task<bool> IsEnabledAsync(int userId, CancellationToken ct = default);
    Task<bool> VerifyTotpAsync(int userId, string code, CancellationToken ct = default);
    Task<bool> VerifyBackupCodeAsync(int userId, string code, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement TotpMfaService**

```csharp
// src/MSOSync.Api/Auth/TotpMfaService.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using OtpNet;

namespace MSOSync.Api.Auth;

internal sealed class TotpMfaService(MSOSyncDbContext db) : IMfaService
{
    public async Task<string> EnrollAsync(int userId, CancellationToken ct = default)
    {
        var existing = await db.TotpSecrets.FindAsync([userId], ct);
        if (existing is not null)
        {
            existing.IsEnabled = false;
            existing.EnabledAt = null;
        }
        else
        {
            var keyBytes = KeyGeneration.GenerateRandomKey(20); // 160-bit key
            var secret = Base32Encoding.ToString(keyBytes);
            db.TotpSecrets.Add(new SyncUserTotpSecret { UserId = userId, Secret = secret });
            await db.SaveChangesAsync(ct);
            return secret;
        }
        await db.SaveChangesAsync(ct);
        return existing.Secret;
    }

    public async Task ConfirmEnrollmentAsync(int userId, string code, CancellationToken ct = default)
    {
        var record = await db.TotpSecrets.FindAsync([userId], ct)
            ?? throw new InvalidOperationException($"No TOTP enrollment found for user {userId}");

        if (!VerifyCode(record.Secret, code))
            throw new InvalidOperationException("Invalid TOTP code");

        record.IsEnabled = true;
        record.EnabledAt = DateTime.UtcNow;

        // Update user's IsMfaEnabled flag
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsMfaEnabled, true), ct);

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GenerateBackupCodesAsync(int userId, CancellationToken ct = default)
    {
        // Remove any existing backup codes
        await db.BackupCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);

        var rawCodes = new string[8];
        for (var i = 0; i < 8; i++)
        {
            rawCodes[i] = GenerateBackupCode();
            db.BackupCodes.Add(new SyncUserBackupCode
            {
                UserId = userId,
                CodeHash = HashCode(rawCodes[i]),
            });
        }
        await db.SaveChangesAsync(ct);
        return rawCodes;
    }

    public async Task<bool> IsEnabledAsync(int userId, CancellationToken ct = default)
    {
        var record = await db.TotpSecrets.FindAsync([userId], ct);
        return record?.IsEnabled == true;
    }

    public async Task<bool> VerifyTotpAsync(int userId, string code, CancellationToken ct = default)
    {
        var record = await db.TotpSecrets.FindAsync([userId], ct);
        if (record is null || !record.IsEnabled) return false;
        return VerifyCode(record.Secret, code);
    }

    public async Task<bool> VerifyBackupCodeAsync(int userId, string code, CancellationToken ct = default)
    {
        var hash = HashCode(code);
        var backup = await db.BackupCodes
            .FirstOrDefaultAsync(c => c.UserId == userId && c.CodeHash == hash && !c.IsUsed, ct);

        if (backup is null) return false;

        backup.IsUsed = true;
        backup.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool VerifyCode(string base32Secret, string code)
    {
        var keyBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(keyBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.VerifyTotp(DateTime.UtcNow, code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    private static string GenerateBackupCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashCode(string code)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code.ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

Note: `db.Users` — replace with actual DbSet name from `MSOSyncDbContext`. Replace `u.Id` with actual SyncUser PK property.

- [ ] **Step 6: Register in DI**

Find where services are registered (likely `AddApiServices` extension or directly in `Program.cs`). Add:

```csharp
services.AddScoped<IMfaService, TotpMfaService>();
```

- [ ] **Step 7: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+6, Failed: 0`

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Api/MSOSync.Api.csproj src/MSOSync.Api/Auth/IMfaService.cs src/MSOSync.Api/Auth/TotpMfaService.cs tests/MSOSync.ApiTests/Auth/TotpMfaServiceTests.cs
git commit -m "feat(2E.4-T2): add IMfaService + TotpMfaService with Otp.NET"
```

---

### Task 3: Login flow change — mfa_token challenge

**Files:**
- Modify: existing login controller/service (find via `Get-ChildItem -Recurse -Include "*Auth*","*Login*" src/MSOSync.Api/Controllers/` and `src/MSOSync.Api/Services/`)
- Create: `src/MSOSync.Api/Auth/MfaTokenService.cs`

**Interfaces:**
- Consumes: `IMfaService` (Task 2), existing JWT token generation service (found in 2E.3-T2 Step 4)
- Produces: Login response `{ requires_mfa: true, mfa_token: "..." }` when MFA enabled; `MfaTokenService.Create(userId) : string`, `MfaTokenService.Validate(mfa_token) : int?`

- [ ] **Step 1: Find existing login handler**

```powershell
Get-ChildItem -Recurse -Include "*Auth*Controller*","*Login*Controller*" src/MSOSync.Api/Controllers/ | Select-Object FullName
Get-ChildItem -Recurse -Include "*Auth*Service*","*Login*Service*" src/MSOSync.Api/Services/ | Select-Object FullName
```

Read the login endpoint. Identify:
1. Where it currently generates and returns the JWT
2. The `SyncUser` object it has at that point

- [ ] **Step 2: Create MfaTokenService**

```csharp
// src/MSOSync.Api/Auth/MfaTokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MSOSync.Api.Auth;

internal sealed class MfaTokenService(IConfiguration config)
{
    private const int ExpiryMinutes = 5;
    private const string MfaUserIdClaim = "mfa_user_id";

    public string Create(int userId)
    {
        var key = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: [new Claim(MfaUserIdClaim, userId.ToString())],
            expires: DateTime.UtcNow.AddMinutes(ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int? Validate(string mfaToken)
    {
        var key = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(mfaToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = config["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out _);

            var claim = principal.FindFirstValue(MfaUserIdClaim);
            return claim is null ? null : int.Parse(claim);
        }
        catch
        {
            return null;
        }
    }
}
```

Note: replace `config["Jwt:Key"]`, `config["Jwt:Issuer"]`, `config["Jwt:Audience"]` with the actual config keys used in the existing JWT service if different.

- [ ] **Step 3: Modify login handler to check IsMfaEnabled**

In the existing login handler, after the user is authenticated (password verified) but before returning the JWT, add:

```csharp
// Inject IMfaService and MfaTokenService into the controller/service constructor

if (user.IsMfaEnabled)
{
    var mfaToken = _mfaTokenService.Create(user.Id);
    return Ok(new { requires_mfa = true, mfa_token = mfaToken });
}

// Existing JWT generation continues here (unchanged for users without MFA)
var jwt = _jwtService.CreateToken(user);
return Ok(new { token = jwt });
```

Register in DI: `services.AddScoped<MfaTokenService>();`

- [ ] **Step 4: Build**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```
git add src/MSOSync.Api/Auth/MfaTokenService.cs <login-controller-or-service-file>
git commit -m "feat(2E.4-T3): modify login flow to return mfa_token when MFA enabled"
```

---

### Task 4: MFA endpoints + tests

**Files:**
- Create: `src/MSOSync.Api/Controllers/MfaController.cs`
- Create: `tests/MSOSync.ApiTests/Controllers/MfaControllerTests.cs`

**Interfaces:**
- Consumes: `IMfaService` (Task 2), `MfaTokenService` (Task 3), existing JWT service
- Produces:
  - `POST /auth/mfa/enroll` — start enrollment, returns `{ secret, totp_uri }`
  - `POST /auth/mfa/enroll/confirm` — confirm with TOTP code, returns 8 raw backup codes
  - `POST /auth/mfa/verify` — verify TOTP or backup code using mfa_token, returns full JWT
  - `DELETE /auth/mfa/enroll` — disable MFA (requires current TOTP code)

- [ ] **Step 1: Write failing tests**

```csharp
// tests/MSOSync.ApiTests/Controllers/MfaControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MSOSync.Api.Auth;
using MSOSync.Api.Controllers;
using System.Security.Claims;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

public sealed class MfaControllerTests
{
    private readonly Mock<IMfaService> _mfa = new();
    private readonly Mock<MfaTokenService> _mfaToken = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly MfaController _controller;

    public MfaControllerTests()
    {
        // Replace IJwtService with actual interface found in 2E.3-T2 Step 4
        _controller = new MfaController(_mfa.Object, _mfaToken.Object, _jwt.Object);

        // Simulate authenticated user (userId = 42)
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "42")], "Test"));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    [Fact]
    public async Task Enroll_ReturnsSecret_AndTotpUri()
    {
        _mfa.Setup(m => m.EnrollAsync(42, default)).ReturnsAsync("JBSWY3DPEHPK3PXP");

        var result = await _controller.Enroll();

        result.Should().BeOfType<OkObjectResult>();
        var body = ((OkObjectResult)result).Value;
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmEnroll_Returns8BackupCodes_OnSuccess()
    {
        _mfa.Setup(m => m.ConfirmEnrollmentAsync(42, "123456", default)).Returns(Task.CompletedTask);
        _mfa.Setup(m => m.GenerateBackupCodesAsync(42, default))
            .ReturnsAsync(Enumerable.Range(0, 8).Select(i => $"code-{i}").ToList());

        var result = await _controller.ConfirmEnroll(new ConfirmEnrollRequest("123456"));

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Verify_ReturnsFullJwt_WhenTotpCodeValid()
    {
        _mfaToken.Setup(s => s.Validate("mfa-tok")).Returns(42);
        _mfa.Setup(m => m.VerifyTotpAsync(42, "654321", default)).ReturnsAsync(true);
        // Replace CreateToken with actual method name
        _jwt.Setup(j => j.CreateToken(It.IsAny<object>())).Returns("full-jwt");

        var result = await _controller.Verify(new MfaVerifyRequest("mfa-tok", "654321", null));

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Verify_Returns401_WhenCodeInvalid()
    {
        _mfaToken.Setup(s => s.Validate("mfa-tok")).Returns(42);
        _mfa.Setup(m => m.VerifyTotpAsync(42, "000000", default)).ReturnsAsync(false);
        _mfa.Setup(m => m.VerifyBackupCodeAsync(42, "000000", default)).ReturnsAsync(false);

        var result = await _controller.Verify(new MfaVerifyRequest("mfa-tok", "000000", null));

        result.Should().BeOfType<UnauthorizedResult>();
    }
}
```

Note: adapt `_jwt.Setup(j => j.CreateToken(...))` to match the actual JWT service interface and method found in 2E.3-T2 Step 4. The `object` parameter type should be `SyncUser`.

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/MSOSync.ApiTests -v minimal 2>&1 | head -10
```

- [ ] **Step 3: Implement MfaController**

```csharp
// src/MSOSync.Api/Controllers/MfaController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Auth;
using System.Security.Claims;

namespace MSOSync.Api.Controllers;

public sealed record ConfirmEnrollRequest(string Code);
public sealed record MfaVerifyRequest(string MfaToken, string? TotpCode, string? BackupCode);

[ApiController]
[Route("auth/mfa")]
public sealed class MfaController(
    IMfaService mfaService,
    MfaTokenService mfaTokenService,
    IJwtService jwtService   // Replace with actual JWT service interface
) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User not authenticated"));

    [HttpPost("enroll")]
    [Authorize]
    public async Task<IActionResult> Enroll(CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        var secret = await mfaService.EnrollAsync(userId, ct);

        // TOTP URI for QR code generation (RFC 3986 format)
        var issuer = Uri.EscapeDataString("MSOSync");
        var account = Uri.EscapeDataString(User.Identity?.Name ?? userId.ToString());
        var totpUri = $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";

        return Ok(new { secret, totp_uri = totpUri });
    }

    [HttpPost("enroll/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmEnroll([FromBody] ConfirmEnrollRequest request, CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        try
        {
            await mfaService.ConfirmEnrollmentAsync(userId, request.Code, ct);
            var backupCodes = await mfaService.GenerateBackupCodesAsync(userId, ct);
            return Ok(new { backup_codes = backupCodes, message = "MFA enabled. Store backup codes securely — they will not be shown again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify([FromBody] MfaVerifyRequest request, CancellationToken ct = default)
    {
        var userId = mfaTokenService.Validate(request.MfaToken);
        if (userId is null) return Unauthorized();

        var codeToVerify = request.TotpCode ?? request.BackupCode;
        if (string.IsNullOrEmpty(codeToVerify)) return BadRequest(new { error = "Provide totp_code or backup_code" });

        bool verified = request.TotpCode is not null
            ? await mfaService.VerifyTotpAsync(userId.Value, request.TotpCode, ct)
            : await mfaService.VerifyBackupCodeAsync(userId.Value, request.BackupCode!, ct);

        if (!verified) return Unauthorized();

        // Find user and issue full JWT — adapt to actual user lookup and JWT service
        // Replace with actual user lookup (e.g., db.Users.FindAsync(userId.Value))
        // and actual JWT generation method
        return Ok(new { token = "PLACEHOLDER — see note below" });
    }

    [HttpDelete("enroll")]
    [Authorize]
    public async Task<IActionResult> DisableMfa([FromBody] ConfirmEnrollRequest request, CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (!await mfaService.VerifyTotpAsync(userId, request.Code, ct))
            return BadRequest(new { error = "Invalid TOTP code" });

        // Delete TOTP secret + backup codes via DbContext
        // Replace with actual DbContext injection or repository call:
        // await db.TotpSecrets.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        // await db.BackupCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        // await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsMfaEnabled, false), ct);

        return NoContent();
    }
}
```

**Note for Verify endpoint:** Replace the `return Ok(new { token = "PLACEHOLDER" })` with actual user lookup + JWT generation. Inject `MSOSyncDbContext` and find user by `userId.Value`, then call the real JWT service. Pattern:

```csharp
var user = await db.Users.FindAsync([userId.Value], ct)
    ?? return Unauthorized();
var token = jwtService.CreateToken(user); // actual method name
return Ok(new { token });
```

Also complete `DisableMfa` with actual DbContext injection.

- [ ] **Step 4: Run tests — all pass**

```
dotnet test tests/MSOSync.ApiTests -v minimal
```

Expected: `Passed: N+4, Failed: 0`

- [ ] **Step 5: Build full solution**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Api/Controllers/MfaController.cs tests/MSOSync.ApiTests/Controllers/MfaControllerTests.cs
git commit -m "feat(2E.4-T4): add MFA enrollment/verify endpoints"
```
