using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[HybridEntity]
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
    public string? ExternalId { get; set; }    // OIDC subject claim
    public string? AuthProvider { get; set; }  // "local" | "oidc:<providerName>"
    public string? Email { get; set; }
}
