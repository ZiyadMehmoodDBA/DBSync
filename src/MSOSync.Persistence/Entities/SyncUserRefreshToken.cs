namespace MSOSync.Persistence.Entities;

public sealed class SyncUserRefreshToken
{
    public long TokenId { get; set; }
    public long UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? FamilyId { get; set; }
}
