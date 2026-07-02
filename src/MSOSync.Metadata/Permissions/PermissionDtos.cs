namespace MSOSync.Metadata.Permissions;

public sealed record EffectivePermissionsDto(
    string Role,
    IReadOnlyList<string> Permissions,
    DateTimeOffset UpdatedAt);

public sealed record PermissionDto(
    string PermissionKey,
    string DisplayName,
    string? Description,
    string Category,
    int SortOrder,
    bool IsSystem);

public sealed record RolePermissionsDto(
    string RoleName,
    int UserCount,
    IReadOnlyList<PermissionDto> Permissions);
