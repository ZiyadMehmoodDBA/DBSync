using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncNodeBootstrapToken : ITenantScoped
{
    public long Id { get; set; }
    public string NodeId { get; set; } = null!;
    public string TokenHash { get; set; } = null!;         // BCrypt hash; raw token never stored
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string IssuedBy { get; set; } = null!;
    public Guid TenantId { get; set; }
}
