using MSOSync.Persistence.Entities;

namespace MSOSync.Security;

public interface IUserService
{
    Task<SyncUser?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task IncrementFailedAttemptsAsync(SyncUser user, CancellationToken ct = default);
    Task LockUserAsync(SyncUser user, DateTime lockedUntil, CancellationToken ct = default);
    Task ResetFailedAttemptsAsync(SyncUser user, CancellationToken ct = default);
    Task UpdateLastLoginAsync(SyncUser user, CancellationToken ct = default);
    Task<List<string>> GetRolesAsync(long userId, CancellationToken ct = default);
}
