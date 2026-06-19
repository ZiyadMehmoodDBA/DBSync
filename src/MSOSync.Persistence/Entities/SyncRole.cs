namespace MSOSync.Persistence.Entities;

public sealed class SyncRole
{
    public long RoleId { get; set; }
    public string RoleName { get; set; } = null!;
}
