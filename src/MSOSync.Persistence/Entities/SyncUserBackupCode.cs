namespace MSOSync.Persistence.Entities;

public sealed class SyncUserBackupCode
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;  // SHA-256 hex of raw code
    public bool IsUsed { get; set; } = false;
    public DateTime? UsedAt { get; set; }

    public SyncUser User { get; set; } = null!;
}
