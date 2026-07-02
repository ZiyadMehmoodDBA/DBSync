namespace MSOSync.Persistence.Entities;

public sealed class SyncRolePermission
{
    public string RoleName      { get; set; } = "";
    public string PermissionKey { get; set; } = "";

    public SyncPermission Permission { get; set; } = null!;
}
