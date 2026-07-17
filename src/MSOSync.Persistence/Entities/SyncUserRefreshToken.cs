using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncUserRefreshToken : ITenantScoped
{
    public long TokenId { get; set; }
    public long UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public string TokenLookupHash { get; set; } = null!;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? FamilyId { get; set; }
    public Guid TenantId { get; set; }
}
