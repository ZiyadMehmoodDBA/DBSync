# Task 5: TenantResolverMiddleware + JWT tenantId Claim + Auth Flow

**Part of:** [Epic 15A Multi-Tenancy](2026-07-16-epic15a-multi-tenancy.md)

**Goal:** Wire tenant resolution into the HTTP pipeline. Add `tenantId` claim to JWT tokens. Add `POST /auth/switch-tenant` endpoint. Implement `ITenantStore` against `AppDbContext`. Wire all DI registrations in `Program.cs`.

**Files:**
- Create: `src/MSOSync.Security/Tenancy/TenantResolverMiddleware.cs`
- Create: `src/MSOSync.Persistence/Tenancy/DbContextTenantStore.cs`
- Create: `src/MSOSync.Persistence/Tenancy/DbContextNodeTenantLookup.cs`
- Modify: `src/MSOSync.Security/JwtService.cs` — add `tenantId` param to `CreateAccessToken`
- Modify: `src/MSOSync.Api/Controllers/AuthController.cs` — pass `tenantId`, add switch-tenant endpoint
- Modify: `src/MSOSync.App/Program.cs` — register DI, add `TenantResolverMiddleware`

**Interfaces:**
- Consumes: `ITenantResolver`, `ITenantAccessValidator`, `ITenantContext`, `TenantAccessException` (Tasks 3, 4); `Tenant`, `TenantMembership`, `AppDbContext` (Task 2); `JwtService` (existing)
- Produces: populated `ITenantContext` per-request, `tenantId` claim in JWT, `POST /auth/switch-tenant` — consumed by Tasks 6, 7, 8

---

- [ ] **Step 1: Create TenantResolverMiddleware**

Create `src/MSOSync.Security/Tenancy/TenantResolverMiddleware.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class TenantResolverMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ITenantResolver resolver)
    {
        try
        {
            var tenantContext = await resolver.ResolveAsync(ctx, ctx.RequestAborted);
            // Register resolved context as scoped so controllers + services + DbContext can inject it
            ctx.RequestServices.GetRequiredService<TenantContextHolder>().Context = tenantContext;
            ctx.Items["IsPlatformContext"] = tenantContext.IsPlatformContext;
        }
        catch (TenantAccessException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
            return;
        }

        await next(ctx);
    }
}

// Scoped holder so DbContext (and any scoped service) can get the resolved context via DI
public sealed class TenantContextHolder
{
    public ITenantContext? Context { get; set; }
}
```

- [ ] **Step 2: Create DbContextTenantStore (implements ITenantStore)**

Create `src/MSOSync.Persistence/Tenancy/DbContextTenantStore.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;
using MSOSync.Security.Tenancy;

namespace MSOSync.Persistence.Tenancy;

public sealed class DbContextTenantStore(AppDbContext db) : ITenantStore
{
    public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken ct)
        => db.Tenants
             .AsNoTracking()
             .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    public Task<TenantMembership?> FindMembershipAsync(Guid tenantId, long userId, CancellationToken ct)
        => db.TenantMemberships
             .AsNoTracking()
             .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);
}
```

> **Important:** `DbContextTenantStore` queries `Tenants` and `TenantMemberships` — these are NOT tenant-scoped (they're platform tables), so global query filters don't apply. The `AsNoTracking()` queries bypass tenant filtering naturally.

- [ ] **Step 3: Create DbContextNodeTenantLookup (implements INodeTenantLookup)**

Create `src/MSOSync.Persistence/Tenancy/DbContextNodeTenantLookup.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Security.Tenancy;

namespace MSOSync.Persistence.Tenancy;

public sealed class DbContextNodeTenantLookup(AppDbContext db) : INodeTenantLookup
{
    public async Task<Guid?> GetNodeTenantIdAsync(string nodeId, CancellationToken ct)
    {
        // IgnoreQueryFilters because the node's TenantId column is not yet populated
        // (it's added in Task 7). After Task 7 this query works normally.
        var node = await db.Nodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.NodeId == nodeId)
            .Select(n => new { n.TenantId })
            .FirstOrDefaultAsync(ct);

        return node?.TenantId;
    }
}
```

- [ ] **Step 4: Create HttpContextCurrentTenantAccessor (implements ICurrentTenantAccessor)**

Create `src/MSOSync.Persistence/Tenancy/HttpContextCurrentTenantAccessor.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Tenancy;
using MSOSync.Security.Tenancy;

namespace MSOSync.Persistence.Tenancy;

// Registered as Singleton. Reads the current request's ITenantContext at EF query time.
// This bridges the EF Core model-cache boundary — the Singleton reference is stable,
// but TenantId is evaluated fresh per query from the current request scope.
public sealed class HttpContextCurrentTenantAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentTenantAccessor
{
    public Guid? TenantId
    {
        get
        {
            var holder = httpContextAccessor.HttpContext?
                .RequestServices?
                .GetService<TenantContextHolder>();

            var ctx = holder?.Context;
            return ctx is { IsPlatformContext: false } ? ctx.TenantId : null;
        }
    }
}
```

- [ ] **Step 5: Modify JwtService.CreateAccessToken to include tenantId**

Open `src/MSOSync.Security/JwtService.cs` and update `CreateAccessToken`:

Find the existing signature:
```csharp
public string CreateAccessToken(long userId, string username, IEnumerable<string> roles)
```

Replace with:
```csharp
public string CreateAccessToken(long userId, string username, IEnumerable<string> roles, Guid? tenantId = null)
{
    var now    = DateTime.UtcNow;
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, username),
        new(ClaimTypes.Name, username),
        new(ClaimTypes.NameIdentifier, username),
        new("userId", userId.ToString()),
        new(JwtRegisteredClaimNames.Iat,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64)
    };

    if (tenantId.HasValue)
        claims.Add(new Claim("tenantId", tenantId.Value.ToString()));

    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

    var token = new JwtSecurityToken(
        issuer:             _issuer,
        audience:           _audience,
        claims:             claims,
        notBefore:          now,
        expires:            now.Add(_accessTokenLifetime),
        signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

- [ ] **Step 6: Update AuthController to pass tenantId + add switch-tenant endpoint**

Open `src/MSOSync.Api/Controllers/AuthController.cs`.

In the existing login endpoint, after authenticating the user, determine their tenant:
- If user has exactly one `TenantMembership` → issue token with that tenantId
- If user has multiple memberships → return `HTTP 300` with tenant picker list (no token yet)
- If user has zero memberships → issue token with no tenantId (platform token or error)

Find the call to `_jwtService.CreateAccessToken(...)` in the login handler and update it:

```csharp
// After successful credential check:
var memberships = await _db.TenantMemberships
    .AsNoTracking()
    .Where(m => m.UserId == user.UserId && m.Status == MemberStatus.Active)
    .Select(m => new { m.TenantId, m.RoleId, TenantSlug = m.Tenant!.Slug })
    .ToListAsync(ct);

if (memberships.Count == 0)
{
    // Platform user or no tenant assigned — issue token without tenantId
    var token = _jwtService.CreateAccessToken(user.UserId, user.Username, roles, tenantId: null);
    return Ok(new { token });
}

if (memberships.Count == 1)
{
    var m     = memberships[0];
    var token = _jwtService.CreateAccessToken(user.UserId, user.Username, roles, tenantId: m.TenantId);
    return Ok(new { token, tenantId = m.TenantId, tenantSlug = m.TenantSlug });
}

// Multiple memberships — return picker list, client must call switch-tenant
return StatusCode(300, new
{
    requiresTenantSelection = true,
    tenants = memberships.Select(m => new { m.TenantId, m.TenantSlug })
});
```

Add the switch-tenant endpoint at the bottom of AuthController:
```csharp
[HttpPost("switch-tenant")]
[Authorize]
public async Task<IActionResult> SwitchTenant(
    [FromBody] SwitchTenantRequest request,
    CancellationToken ct)
{
    var userIdClaim = User.FindFirstValue("userId");
    if (!long.TryParse(userIdClaim, out var userId))
        return Unauthorized();

    // Validate the new tenant membership
    var membership = await _db.TenantMemberships
        .AsNoTracking()
        .Include(m => m.Tenant)
        .FirstOrDefaultAsync(m => m.TenantId == request.TenantId
                               && m.UserId   == userId
                               && m.Status   == MemberStatus.Active, ct);

    if (membership is null || membership.Tenant?.Status != TenantStatus.Active)
        return Forbid();

    var user  = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);
    if (user is null) return Unauthorized();

    var roles = await _db.UserRoles
        .AsNoTracking()
        .Where(ur => ur.UserId == userId)
        .Join(_db.Roles, ur => ur.RoleId, r => r.RoleId, (_, r) => r.RoleName)
        .ToListAsync(ct);

    var token = _jwtService.CreateAccessToken(userId, user.Username, roles, tenantId: request.TenantId);
    return Ok(new { token, tenantId = request.TenantId, tenantSlug = membership.Tenant!.Slug });
}

public sealed record SwitchTenantRequest(Guid TenantId);
```

- [ ] **Step 7: Register DI and add middleware in Program.cs**

Open `src/MSOSync.App/Program.cs`.

After existing service registrations, add:
```csharp
// Tenancy
builder.Services.AddScoped<TenantContextHolder>();
builder.Services.AddScoped<ITenantResolver,        TenantResolver>();
builder.Services.AddScoped<ITenantAccessValidator, TenantAccessValidator>();
builder.Services.AddScoped<ITenantStore,           DbContextTenantStore>();
builder.Services.AddScoped<INodeTenantLookup,      DbContextNodeTenantLookup>();
builder.Services.AddSingleton<ICurrentTenantAccessor, HttpContextCurrentTenantAccessor>();
```

In the middleware pipeline, add `TenantResolverMiddleware` AFTER `UseAuthentication()` and BEFORE `UseAuthorization()`:
```csharp
app.UseAuthentication();
app.UseMiddleware<TenantResolverMiddleware>();   // ← add this line
app.UseAuthorization();
app.MapControllers();
```

Required usings to add at the top of Program.cs:
```csharp
using MSOSync.Security.Tenancy;
using MSOSync.Persistence.Tenancy;
using MSOSync.Common.Tenancy;
```

- [ ] **Step 8: Add CE startup guard**

In Program.cs, after `app.Build()` and before `app.Run()`, add:
```csharp
// CE guard: verify SystemTenant exists — fatal if missing
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var systemTenantExists = await db.Tenants
        .AnyAsync(t => t.TenantId == WellKnownTenantIds.SystemTenant);

    if (!systemTenantExists)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogCritical("SystemTenant not found in database. Run migrations before starting the application.");
        throw new InvalidOperationException("SystemTenant missing — database migration required");
    }
}
```

Required using: `using MSOSync.Common.Tenancy;`

- [ ] **Step 9: Build the full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```
Expected: `Build succeeded. 0 Error(s)` (or warnings only)

Fix any compile errors before proceeding — common issues:
- Missing namespace imports in AuthController
- `_db` may be named differently — check existing field names in AuthController

- [ ] **Step 10: Commit**

```
git add src/MSOSync.Security/Tenancy/TenantResolverMiddleware.cs
git add src/MSOSync.Persistence/Tenancy/DbContextTenantStore.cs src/MSOSync.Persistence/Tenancy/DbContextNodeTenantLookup.cs src/MSOSync.Persistence/Tenancy/HttpContextCurrentTenantAccessor.cs
git add src/MSOSync.Security/JwtService.cs
git add src/MSOSync.Api/Controllers/AuthController.cs
git add src/MSOSync.App/Program.cs
git commit -m "feat(15A-5): TenantResolverMiddleware, JWT tenantId claim, switch-tenant endpoint, DI wiring"
```
