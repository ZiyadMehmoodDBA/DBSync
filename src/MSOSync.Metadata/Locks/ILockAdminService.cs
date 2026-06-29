namespace MSOSync.Metadata.Locks;

public interface ILockAdminService
{
    Task<IReadOnlyList<LockDto>> GetLocksAsync(CancellationToken ct);
    Task<bool>                   DeleteLockAsync(string lockName, CancellationToken ct);
}
