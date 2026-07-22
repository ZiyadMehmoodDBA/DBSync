using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncReplayItem : ITenantScoped
{
    public Guid    ItemId          { get; set; }
    public Guid    OperationId     { get; set; }
    public long?   SourceBatchId   { get; set; }   // null for MissedData
    public long?   ReplayBatchId   { get; set; }   // null until worker processes item
    public string  NodeId          { get; set; } = null!;
    public string  ChannelId       { get; set; } = null!;
    public int     EventCount      { get; set; }
    public string  Status          { get; set; } = null!; // Pending|Processing|Completed|Failed|Skipped
    public string? ErrorMessage    { get; set; }
    public Guid    TenantId        { get; set; }
}
