namespace MSOSync.Persistence.Entities;

public sealed class SyncRouter
{
    public string RouterId { get; set; } = null!;
    public string SourceNodeGroup { get; set; } = null!;
    public string TargetNodeGroup { get; set; } = null!;
    public string RouterType { get; set; } = "default";
    public bool Enabled { get; set; } = true;
}
