namespace MSOSync.Persistence.Entities;

public sealed class SyncServiceAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;              // Unique name per tenant
    public string? PermissionsJson { get; set; }                     // Stores JSON-serialized string[] of permissions
    public string ClientId { get; set; } = string.Empty;          // Public identifier (UUID format)
    public string ClientSecretHash { get; set; } = string.Empty;  // SHA-256 hex of secret
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }                    // Why it was revoked
}
