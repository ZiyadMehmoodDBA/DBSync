using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security.Events;

namespace MSOSync.Security;

public sealed class AuthenticationService(
    IUserService userService,
    JwtService jwtService,
    BCryptPasswordHasher hasher,
    AppDbContext db,
    IMediator mediator)
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
    private static readonly int[] LoginDelaysMs = [0, 1000, 2000, 4000];

    public async Task<LoginResult> LoginAsync(
        string username, string password, string correlationId,
        CancellationToken ct = default)
    {
        var user = await userService.FindByUsernameAsync(username, ct);

        if (user == null || !user.Enabled)
        {
            await ApplyLoginDelayAsync(0, ct);
            await mediator.Publish(new LoginFailureEvent(username, correlationId), ct);
            return new LoginResult(false, null, null, null, "Invalid credentials");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            return new LoginResult(false, null, null, null,
                $"Account locked until {user.LockedUntil:u}");
        }

        await ApplyLoginDelayAsync(user.FailedAttempts, ct);

        if (!hasher.Verify(password, user.PasswordHash))
        {
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

        var roles = await userService.GetRolesAsync(user.UserId, ct);
        var accessToken = jwtService.CreateAccessToken(user.UserId, user.Username, roles);
        var (rawRefreshToken, refreshEntity) = CreateRefreshToken(user.UserId, familyId: null);

        db.UserRefreshTokens.Add(refreshEntity);
        await db.SaveChangesAsync(ct);

        await mediator.Publish(new LoginSuccessEvent(username, correlationId), ct);

        return new LoginResult(true, accessToken, rawRefreshToken, refreshEntity.ExpiresAt, null);
    }

    public async Task<RefreshResult> RefreshAsync(
        string rawRefreshToken, string correlationId,
        CancellationToken ct = default)
    {
        var all = await db.UserRefreshTokens
            .AsNoTracking()
            .Where(t => t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        var existing = all.FirstOrDefault(t =>
            hasher.Verify(rawRefreshToken, t.TokenHash));

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

        var roles = await userService.GetRolesAsync(user.UserId, ct);
        var accessToken = jwtService.CreateAccessToken(user.UserId, user.Username, roles);

        var childFamilyId = existing.FamilyId ?? existing.TokenId;
        var (rawNew, newRefreshEntity) = CreateRefreshToken(user.UserId, familyId: childFamilyId);

        db.UserRefreshTokens.Add(newRefreshEntity);
        await db.SaveChangesAsync(ct);

        return new RefreshResult(true, accessToken, rawNew, newRefreshEntity.ExpiresAt, null);
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var tokens = await db.UserRefreshTokens
            .AsNoTracking()
            .Where(t => t.RevokedAt == null)
            .ToListAsync(ct);

        var match = tokens.FirstOrDefault(t => hasher.Verify(rawRefreshToken, t.TokenHash));
        if (match == null) return;

        await db.UserRefreshTokens
            .Where(t => t.TokenId == match.TokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);
    }

    private (string RawToken, SyncUserRefreshToken Entity) CreateRefreshToken(long userId, long? familyId)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime);
        return (raw, new SyncUserRefreshToken
        {
            UserId = userId,
            TokenHash = hasher.Hash(raw),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            FamilyId = familyId
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
