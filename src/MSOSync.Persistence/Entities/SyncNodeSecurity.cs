namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeSecurity
{
    public string NodeId { get; set; } = null!;
    public string NodeToken { get; set; } = null!;
    public string CurrentTokenHash { get; set; } = null!;
    public string? NextTokenHash { get; set; }
    public DateTime? CreatedTime { get; set; }
}
