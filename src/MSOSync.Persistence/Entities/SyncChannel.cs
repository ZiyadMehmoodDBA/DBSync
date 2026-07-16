using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncChannel : ITenantScoped
{
    public string ChannelId { get; set; } = null!;
    public int Priority { get; set; }
    public int BatchSize { get; set; } = 1000;
    public int MaxBatchToSend { get; set; } = 10;
    public long MaxDataSize { get; set; } = 1048576L;
    public bool Enabled { get; set; } = true;

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
