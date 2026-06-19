namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeSecurity
{
    public string NodeId { get; set; } = null!;
    public string NodeToken { get; set; } = null!;
    public DateTime? CreatedTime { get; set; }
}
