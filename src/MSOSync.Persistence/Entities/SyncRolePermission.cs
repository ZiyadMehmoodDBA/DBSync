using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[GlobalEntity]
public sealed class SyncRolePermission
{
    public string RoleName      { get; set; } = "";
    public string PermissionKey { get; set; } = "";

    public SyncPermission Permission { get; set; } = null!;
}
