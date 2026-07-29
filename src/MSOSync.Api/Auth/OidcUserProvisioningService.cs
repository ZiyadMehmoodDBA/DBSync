using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

internal sealed class OidcUserProvisioningService(AppDbContext db) : IOidcUserProvisioningService
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

        // Username prefixed with "oidc:<provider>:" to prevent collisions with local users
        // on the UQ_sync_user_username index.
        // PasswordHash set to "!oidc" — a sentinel BCrypt rejects immediately without throwing
        // SaltParseException (it doesn't start with "$2" so BCrypt won't attempt to parse it).
        var user = new SyncUser
        {
            ExternalId   = sub,
            AuthProvider = authProvider,
            Email        = email,
            Username     = $"oidc:{providerName}:{sub}",
            PasswordHash = "!oidc",
        };
        // Step 1: insert user and materialize the identity-generated UserId.
        // SyncUserRole.UserId is ValueGeneratedNever() with no FK navigation, so EF
        // cannot do identity fixup — we must flush first to get the real PK value.
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        // Step 2: assign default VIEWER role using the now-materialized UserId.
        // Role must exist in the sync_role table (seeded by migrations).
        var viewerRole = await db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleName == "VIEWER", ct);
        if (viewerRole is not null)
        {
            db.UserRoles.Add(new SyncUserRole
            {
                UserId = user.UserId,
                RoleId = viewerRole.RoleId,
            });
            await db.SaveChangesAsync(ct);
        }

        return user;
    }
}
