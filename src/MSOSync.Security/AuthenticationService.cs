using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security.Events;

namespace MSOSync.Security;

public sealed class AuthenticationService(
    IUserService userService,
    JwtService jwtService,
    BCryptPasswordHasher hasher,
    AppDbContext db,
    IMediator mediator,
    AuthMetrics metrics,
    IConfiguration configuration)
{
    private readonly TimeSpan _refreshTokenLifetime =
        TimeSpan.FromDays(configuration.GetValue<int>("Jwt:RefreshExpiryDays", 7));
    private static readonly int[] LoginDelaysMs = [0, 1000, 2000, 4000];

    public async Task<LoginResult> LoginAsync(
        string username, string password, string correlationId,
        CancellationToken ct = default)
    {
        metrics.LoginAttempts.Add(1);

        var user = await userService.FindByUsernameAsync(username, ct);

        if (user == null || !user.Enabled)
        {
            metrics.LoginFailures.Add(1);
            await ApplyLoginDelayAsync(0, ct);
            await mediator.Publish(new LoginFailureEvent(username, correlationId), ct);
            return new LoginResult(false, null, null, null, "Invalid credentials");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            metrics.LoginFailures.Add(1);
            return new LoginResult(false, null, null, null,
                $"Account locked until {user.LockedUntil:u}");
        }

        await ApplyLoginDelayAsync(user.FailedAttempts, ct);

        if (!hasher.Verify(password, user.PasswordHash))
        {
            metrics.LoginFailures.Add(1);
            var newAttempts = user.FailedAttempts + 1;
            if (newAttempts >= 5)
            {
                await userService.LockUserAsync(user, DateTime.UtcNow.AddMinutes(15), ct);
                await mediator.Publish(new AccountLockedEvent(username, correlationId), ct);
                return new LoginResult(false, null, null, null,
                    "Account locked due to too many failed attempts");
            }
            await userService.IncrementFailedAttemptsAsync(user, ct);
            await mediator.Publish(new LoginFailureEvent(username, correlationId), ct);
            return new LoginResult(false, null, null, null, "Invalid credentials");
        }

        await userService.ResetFailedAttemptsAsync(user, ct);
        await userService.UpdateLastLoginAsync(user, ct);

        // MFA challenge: if the user has TOTP enabled, return a short-lived challenge result.
        // The controller will exchange this for an mfa_token; no access token is issued here.
        if (user.IsMfaEnabled)
        {
            await mediator.Publish(new LoginSuccessEvent(username, correlationId), ct);
            return new LoginResult(
                Success: true,
                AccessToken: null,
                RefreshToken: null,
                ExpiresAt: null,
                Error: null,
                RequiresMfa: true,
                UserId: user.UserId);
        }

        var roles = await userService.GetRolesAsync(user.UserId, ct);

        // Resolve tenant membership for multi-tenant token issuance
        var memberships = await db.TenantMemberships
            .AsNoTracking()
            .Where(m => m.UserId == user.UserId && m.Status == MemberStatus.Active)
            .Select(m => new { m.TenantId, TenantSlug = m.Tenant!.Slug })
            .ToListAsync(ct);

        Guid? resolvedTenantId = null;
        string? resolvedTenantSlug = null;
        IReadOnlyList<TenantPickerItem>? tenantPicker = null;

        if (memberships.Count == 1)
        {
            resolvedTenantId   = memberships[0].TenantId;
            resolvedTenantSlug = memberships[0].TenantSlug;
        }
        else if (memberships.Count > 1)
        {
            tenantPicker = memberships.Select(m => new TenantPickerItem(m.TenantId, m.TenantSlug)).ToList();
            // Issue no token yet — client must call switch-tenant after selection
            var (rawRefreshToken2, refreshEntity2) = CreateRefreshToken(user.UserId, familyId: null);
            db.UserRefreshTokens.Add(refreshEntity2);
            await db.SaveChangesAsync(ct);
            await mediator.Publish(new LoginSuccessEvent(username, correlationId), ct);
            return new LoginResult(
                Success: true,
                AccessToken: null,
                RefreshToken: rawRefreshToken2,
                ExpiresAt: refreshEntity2.ExpiresAt,
                Error: null,
                RequiresTenantSelection: true,
                Tenants: tenantPicker);
        }
        // else memberships.Count == 0 → platform token (no tenantId)

        var accessToken  = jwtService.CreateAccessToken(user.UserId, user.Username, roles, tenantId: resolvedTenantId);
        var (rawRefreshToken, refreshEntity) = CreateRefreshToken(user.UserId, familyId: null);

        db.UserRefreshTokens.Add(refreshEntity);
        await db.SaveChangesAsync(ct);

        await mediator.Publish(new LoginSuccessEvent(username, correlationId), ct);

        return new LoginResult(
            Success: true,
            AccessToken: accessToken,
            RefreshToken: rawRefreshToken,
            ExpiresAt: refreshEntity.ExpiresAt,
            Error: null,
            TenantId: resolvedTenantId,
            TenantSlug: resolvedTenantSlug);
    }

    public async Task<RefreshResult> RefreshAsync(
        string rawRefreshToken, string correlationId,
        CancellationToken ct = default)
    {
        metrics.RefreshTotal.Add(1);

        var lookupHash = ComputeLookupHash(rawRefreshToken);

        var existing = await db.UserRefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenLookupHash == lookupHash && t.ExpiresAt > DateTime.UtcNow, ct);

        if (existing == null)
            return new RefreshResult(false, null, null, null, "Invalid refresh token");

        if (existing.RevokedAt.HasValue)
        {
            var familyId = existing.FamilyId ?? existing.TokenId;
            await RevokeTokenFamilyAsync(familyId, ct);

            var user2 = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == existing.UserId, ct);
            if (user2 != null)
                await mediator.Publish(
                    new TokenReuseDetectedEvent(user2.Username, familyId, correlationId), ct);

            return new RefreshResult(false, null, null, null, "Token reuse detected — all sessions revoked");
        }

        await db.UserRefreshTokens
            .Where(t => t.TokenId == existing.TokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == existing.UserId, ct);

        if (user == null || !user.Enabled)
            return new RefreshResult(false, null, null, null, "User not found or disabled");

        var roles       = await userService.GetRolesAsync(user.UserId, ct);
        var accessToken = jwtService.CreateAccessToken(user.UserId, user.Username, roles);

        var childFamilyId = existing.FamilyId ?? existing.TokenId;
        var (rawNew, newRefreshEntity) = CreateRefreshToken(user.UserId, familyId: childFamilyId);

        db.UserRefreshTokens.Add(newRefreshEntity);
        await db.SaveChangesAsync(ct);

        return new RefreshResult(true, accessToken, rawNew, newRefreshEntity.ExpiresAt, null);
    }

    public async Task LogoutAsync(string rawRefreshToken, long callerUserId, CancellationToken ct = default)
    {
        var lookupHash = ComputeLookupHash(rawRefreshToken);

        var match = await db.UserRefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenLookupHash == lookupHash && t.RevokedAt == null, ct);

        if (match == null || match.UserId != callerUserId) return;

        await db.UserRefreshTokens
            .Where(t => t.TokenId == match.TokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
    }

    internal static string ComputeLookupHash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLower();

    private (string RawToken, SyncUserRefreshToken Entity) CreateRefreshToken(long userId, long? familyId)
    {
        var raw       = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiresAt = DateTime.UtcNow.Add(_refreshTokenLifetime);
        return (raw, new SyncUserRefreshToken
        {
            UserId          = userId,
            TokenHash       = hasher.Hash(raw),
            TokenLookupHash = ComputeLookupHash(raw),
            IssuedAt        = DateTime.UtcNow,
            ExpiresAt       = expiresAt,
            FamilyId        = familyId
        });
    }

    private Task RevokeTokenFamilyAsync(long familyId, CancellationToken ct) =>
        db.UserRefreshTokens
            .Where(t => (t.FamilyId == familyId || t.TokenId == familyId) && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);

    private static Task ApplyLoginDelayAsync(int failedAttempts, CancellationToken ct)
    {
        var delayMs = LoginDelaysMs[Math.Min(failedAttempts, LoginDelaysMs.Length - 1)];
        return delayMs > 0 ? Task.Delay(delayMs, ct) : Task.CompletedTask;
    }
}
