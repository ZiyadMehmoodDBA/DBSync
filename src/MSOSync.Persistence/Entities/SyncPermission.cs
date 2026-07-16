using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[GlobalEntity]
public sealed class SyncPermission
{
    public string  PermissionKey { get; set; } = "";
    public string  DisplayName   { get; set; } = "";
    public string? Description   { get; set; }
    public string  Category      { get; set; } = "";
    public int     SortOrder     { get; set; }
    public bool    IsSystem      { get; set; } = true;

    public ICollection<SyncRolePermission> RolePermissions { get; set; } = [];
}
