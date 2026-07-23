namespace MSOSync.Common.Locks;

public sealed class DistributedLockOptions
{
    public const string SectionName = "DistributedLocks";

    /// <summary>"Sql" or "Redis". Defaults to "Sql".</summary>
    public string   Provider      { get; set; } = "Sql";

    /// <summary>Default TTL when callers use the convenience helpers.</summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of retry attempts for TryAcquireWithRetryAsync (not used by
    /// TryAcquireAsync itself). Default 3.
    /// </summary>
    public int      RetryCount    { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts. Default 200 ms.
    /// </summary>
    public TimeSpan RetryDelay    { get; set; } = TimeSpan.FromMilliseconds(200);
}
