namespace MSOSync.Common.Locks;

public static class DistributedLockHelper
{
    /// <summary>
    /// Attempt to acquire with retry. Returns null if all attempts fail.
    /// </summary>
    public static async Task<IDistributedLock?> TryAcquireWithRetryAsync(
        this IDistributedLockService service,
        string                       resource,
        string                       owner,
        DistributedLockOptions       options,
        CancellationToken            ct = default)
    {
        for (var attempt = 0; attempt <= options.RetryCount; attempt++)
        {
            var handle = await service.TryAcquireAsync(
                resource, owner, options.DefaultExpiry, ct);
            if (handle is not null) return handle;

            if (attempt < options.RetryCount)
                await Task.Delay(options.RetryDelay, ct);
        }
        return null;
    }
}
