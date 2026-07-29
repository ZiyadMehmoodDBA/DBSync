namespace MSOSync.Security;

public sealed record LoginResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    string? Error,
    Guid? TenantId = null,
    string? TenantSlug = null,
    bool RequiresTenantSelection = false,
    IReadOnlyList<TenantPickerItem>? Tenants = null,
    bool RequiresMfa = false,
    long? UserId = null);

public sealed record TenantPickerItem(Guid TenantId, string TenantSlug);
