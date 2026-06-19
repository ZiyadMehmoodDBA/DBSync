# Epic 4 / Task 2: ICurrentUserService + ClaimTypes.Name in JWT

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ICurrentUserService` to `MSOSync.Common`, implement `HttpContextCurrentUserService` in `MSOSync.App`, add `ClaimTypes.Name` claim to JWT access tokens so `Identity.Name` resolves to the username, and register the service in `Program.cs`.

**Architecture:** Interface lives in `Common` (no framework dependencies). Implementation in `App` uses `IHttpContextAccessor`. Background jobs get `"system"` when no HTTP context exists. Service does NOT know how JWTs are structured — it just reads `Identity.Name`.

**Tech Stack:** `Microsoft.AspNetCore.Http.IHttpContextAccessor`, `System.Security.Claims.ClaimTypes`

## Global Constraints

- `ICurrentUserService` in `MSOSync.Common` namespace — no ASP.NET Core dependencies
- Implementation in `MSOSync.App` namespace — already has access to Common via transitive reference
- `Identity.Name` resolves only if `ClaimTypes.Name` claim is present in JWT
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A`
- dotnet PATH (BOTH required):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Common/ICurrentUserService.cs`
- Create: `src/MSOSync.App/HttpContextCurrentUserService.cs`
- Modify: `src/MSOSync.Security/JwtService.cs` — add `ClaimTypes.Name` claim
- Modify: `src/MSOSync.App/Program.cs` — register `IHttpContextAccessor` + `ICurrentUserService`

**Interfaces:**
- Consumes: `IHttpContextAccessor` (ASP.NET Core), `ClaimTypes.Name` (from JWT after this task)
- Produces: `ICurrentUserService.GetCurrentUsername() → string` — consumed by Tasks 4 and 5

---

- [ ] **Step 1: Create `ICurrentUserService` in `MSOSync.Common`**

```csharp
// src/MSOSync.Common/ICurrentUserService.cs
namespace MSOSync.Common;

public interface ICurrentUserService
{
    string GetCurrentUsername();
}
```

- [ ] **Step 2: Create `HttpContextCurrentUserService` in `MSOSync.App`**

```csharp
// src/MSOSync.App/HttpContextCurrentUserService.cs
using Microsoft.AspNetCore.Http;
using MSOSync.Common;

namespace MSOSync.App;

public sealed class HttpContextCurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string GetCurrentUsername() =>
        accessor.HttpContext?.User?.Identity?.Name ?? "system";
}
```

- [ ] **Step 3: Add `ClaimTypes.Name` claim to `JwtService.CreateAccessToken`**

In `src/MSOSync.Security/JwtService.cs`, the `CreateAccessToken` method builds a claims list. Add `new Claim(ClaimTypes.Name, username)` immediately after the existing `JwtRegisteredClaimNames.Sub` claim. Full method after edit:

```csharp
public string CreateAccessToken(long userId, string username, IEnumerable<string> roles)
{
    var now = DateTime.UtcNow;
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, username),
        new(ClaimTypes.Name, username),
        new("userId", userId.ToString()),
        new(JwtRegisteredClaimNames.Iat,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64)
    };
    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

    var token = new JwtSecurityToken(
        claims: claims,
        notBefore: now,
        expires: now.Add(AccessTokenLifetime),
        signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

`ClaimTypes` is in `System.Security.Claims` — already imported in the file.

- [ ] **Step 4: Register `IHttpContextAccessor` and `ICurrentUserService` in `Program.cs`**

In `src/MSOSync.App/Program.cs`, add these two lines immediately after `builder.Services.AddSecurity(builder.Configuration);`:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
```

Also add the using at the top of the file:

```csharp
using MSOSync.Common;
```

Full `Program.cs` after edit (showing context around the additions):

```csharp
using FluentValidation;
using FluentValidation.AspNetCore;
using MSOSync.Api.Controllers.Auth;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Security;
using Serilog;

// ... (bootstrap logger, exitCode, try block unchanged) ...

    builder.Services.AddPersistence(builder.Configuration);
    builder.Services.AddSecurity(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

    builder.Services.AddControllers()
        .AddApplicationPart(typeof(AuthController).Assembly);
    // ... rest unchanged
```

- [ ] **Step 5: Build full solution**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet build MSOSync.sln
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 6: Run all tests — existing security tests must still pass**

```powershell
dotnet test MSOSync.sln --no-build
```

Expected: all previously passing tests still pass (login/refresh/logout integration tests depend on JwtService — they must still work after adding the extra claim)

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Common/ICurrentUserService.cs
git add src/MSOSync.App/HttpContextCurrentUserService.cs
git add src/MSOSync.Security/JwtService.cs
git add src/MSOSync.App/Program.cs
git commit -m "feat(common): add ICurrentUserService + HttpContextCurrentUserService; add ClaimTypes.Name to JWT"
```
