namespace MSOSync.Persistence.Entities;

public sealed class SyncUserTotpSecret
{
    public long UserId { get; set; }
    public string Secret { get; set; } = string.Empty;   // base32-encoded
    public bool IsEnabled { get; set; } = false;
    public DateTime? EnabledAt { get; set; }

    public SyncUser User { get; set; } = null!;
}
