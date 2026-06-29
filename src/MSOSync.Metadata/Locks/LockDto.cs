namespace MSOSync.Metadata.Locks;

public sealed record LockDto(
    string    LockName,
    string?   LockOwner,
    DateTime? LockTime);
