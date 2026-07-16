using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[GlobalEntity]
public sealed class SyncLock
{
    public string LockName { get; set; } = null!;
    public string? LockOwner { get; set; }
    public DateTime? LockTime { get; set; }
}
