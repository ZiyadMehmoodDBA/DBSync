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
        var user = new SyncUser
        {
            ExternalId    = sub,
            AuthProvider  = authProvider,
            Email         = email,
            Username      = email,
            PasswordHash  = string.Empty,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
