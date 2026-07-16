using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public enum LockScope { Platform, Tenant }

[GlobalEntity]
public sealed class SyncLock
{
    public string LockName { get; set; } = null!;
    public string? LockOwner { get; set; }
    public DateTime? LockTime { get; set; }
    public LockScope Scope { get; set; } = LockScope.Platform;
}
