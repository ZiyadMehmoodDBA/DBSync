using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncReplayRequest : ITenantScoped
{
    public Guid    ReplayId        { get; set; }
    public Guid    OperationId     { get; set; }
    public string  NodeId          { get; set; } = null!;
    public string? ChannelIdsJson  { get; set; }    // JSON string[]; null = all channels
    public string? BatchIdsJson    { get; set; }    // JSON long[]; null = no cherry-pick
    public DateTime FromTime       { get; set; }
    public DateTime ToTime         { get; set; }
    public string  ReplayMode      { get; set; } = null!; // FailedDelivery|MissedData|Both
    public Guid    TenantId        { get; set; }
}
