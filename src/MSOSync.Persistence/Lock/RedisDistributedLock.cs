using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

internal sealed class RedisDistributedLock : IDistributedLock
{
    private readonly RedisDistributedLockService _service;
    private bool _disposed;

    public string         Resource  { get; }
    public string         Owner     { get; }
    public DateTimeOffset ExpiresAt { get; }

    internal RedisDistributedLock(
        RedisDistributedLockService service,
        string                      resource,
        string                      owner,
        DateTimeOffset              expiresAt)
    {
        _service  = service;
        Resource  = resource;
        Owner     = owner;
        ExpiresAt = expiresAt;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _service.ReleaseAsync(Resource, Owner, CancellationToken.None);
    }
}
