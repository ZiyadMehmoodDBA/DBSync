namespace MSOSync.Persistence.Entities;

public sealed class SyncRegistrationRequest
{
    public long RequestId { get; set; }
    public string NodeId { get; set; } = null!;
    public string? NodeGroup { get; set; }
    public string? SyncUrl { get; set; }
    public string? NodeVersion { get; set; }
    public string? DbType { get; set; }
    public DateTime? RequestTime { get; set; }
    public bool Approved { get; set; } = false;
}
