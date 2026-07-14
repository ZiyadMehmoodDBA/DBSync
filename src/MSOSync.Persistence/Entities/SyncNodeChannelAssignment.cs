namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeChannelAssignment
{
    public string NodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
}
