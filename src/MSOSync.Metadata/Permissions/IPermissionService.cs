namespace MSOSync.Metadata.Permissions;

public interface IPermissionService
{
    Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken ct = default);
    Task<RolePermissionsDto> GetRolePermissionsAsync(string roleName, CancellationToken ct = default);
    Task<IReadOnlyList<RolePermissionsDto>> GetAllRolesAsync(CancellationToken ct = default);
    Task GrantPermissionAsync(string roleName, string permissionKey, CancellationToken ct = default);
    Task RevokePermissionAsync(string roleName, string permissionKey, CancellationToken ct = default);
    Task ResetRoleToDefaultsAsync(string roleName, CancellationToken ct = default);
    Task CopyPermissionsFromAsync(string targetRole, string sourceRole, CancellationToken ct = default);
}
