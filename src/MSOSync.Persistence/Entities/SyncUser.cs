namespace MSOSync.Persistence.Entities;

public sealed class SyncUser
{
    public long UserId { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public int FailedAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public DateTime? CreatedTime { get; set; }
}
