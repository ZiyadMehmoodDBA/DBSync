using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.Users;

public sealed class UsersManagementService(
    AppDbContext         db,
    BCryptPasswordHasher hasher) : IUsersManagementService
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<UserSummaryDto>> GetUsersAsync(
        int page, int pageSize, bool? enabled, string? search,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page     = Math.Max(1, page);

        var query = db.Users.AsNoTracking();

        if (enabled.HasValue)
            query = query.Where(u => u.Enabled == enabled.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => EF.Functions.Like(u.Username, search + "%"));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(u => u.UserId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserSummaryDto(
                u.UserId, u.Username, u.Enabled, u.LastLogin, u.LockedUntil))
            .ToListAsync(ct);

        return new PagedResult<UserSummaryDto>(items.AsReadOnly(), page, pageSize, total);
    }

    public async Task<UserDetailDto?> GetUserAsync(long userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        return u == null ? null : MapDetail(u);
    }

    public async Task<UserDetailDto> CreateUserAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        var exists = await db.Users.AnyAsync(u => u.Username == request.Username, ct);
        if (exists)
            throw new InvalidOperationException(
                "Username already taken");

        var user = new SyncUser
        {
            Username          = request.Username,
            PasswordHash      = hasher.Hash(request.Password),
            Enabled           = request.Enabled,
            PasswordChangedAt = DateTime.UtcNow,
            CreatedTime       = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return MapDetail(user);
    }

    public async Task<UserDetailDto> UpdateUserAsync(
        long userId, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException($"User {userId} not found", "USER_NOT_FOUND");

        if (request.Enabled.HasValue)
        {
            user.Enabled = request.Enabled.Value;
            if (request.Enabled.Value)
            {
                user.LockedUntil    = null;
                user.FailedAttempts = 0;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            user.PasswordHash      = hasher.Hash(request.NewPassword);
            user.PasswordChangedAt = DateTime.UtcNow;
        }

        var dto = MapDetail(user);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            await db.UserRefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);

            db.ChangeTracker.Clear();
        }

        return dto;
    }

    public async Task DeactivateUserAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new NotFoundException($"User {userId} not found", "USER_NOT_FOUND");

        user.Enabled = false;
        await db.SaveChangesAsync(ct);

        await db.UserRefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), ct);

        db.ChangeTracker.Clear();
    }

    private static UserDetailDto MapDetail(SyncUser u) =>
        new(u.UserId, u.Username, u.Enabled, u.LastLogin,
            u.FailedAttempts, u.LockedUntil, u.PasswordChangedAt, u.CreatedTime);
}
