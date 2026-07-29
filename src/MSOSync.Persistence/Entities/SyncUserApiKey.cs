namespace MSOSync.Persistence.Entities;

public sealed class SyncUserApiKey
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public string KeyHash { get; set; } = string.Empty;      // SHA-256 hex of full key
    public string KeyPrefix { get; set; } = string.Empty;    // First 8 chars of key for display
    public string Name { get; set; } = string.Empty;         // User-friendly name
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }

    public SyncUser User { get; set; } = null!;
}
