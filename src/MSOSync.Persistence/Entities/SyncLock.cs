namespace MSOSync.Persistence.Entities;

public sealed class SyncLock
{
    public string LockName { get; set; } = null!;
    public string? LockOwner { get; set; }
    public DateTime? LockTime { get; set; }
}
