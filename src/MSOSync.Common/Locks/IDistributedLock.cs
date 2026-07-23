namespace MSOSync.Common.Locks;

/// <summary>
/// Handle to an acquired distributed lock. Dispose to release.
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    string         Resource  { get; }
    string         Owner     { get; }
    DateTimeOffset ExpiresAt { get; }
}
