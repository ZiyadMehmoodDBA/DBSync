using System.Collections.Concurrent;

namespace MSOSync.Metadata.Lifecycle;

/// <summary>
/// Per-node in-process serialization (single-instance hub assumption, spec §1 non-goals).
/// RowVersion optimistic concurrency remains the cross-process guard.
/// </summary>
public sealed class NodeLifecycleLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string nodeId, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) sem.Release();
        }
    }
}
