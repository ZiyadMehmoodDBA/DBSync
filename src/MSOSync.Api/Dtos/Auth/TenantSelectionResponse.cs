using MSOSync.Security;

namespace MSOSync.Api.Dtos.Auth;

public sealed record TenantSelectionResponse(
    bool RequiresTenantSelection,
    string? RefreshToken,
    IReadOnlyList<TenantPickerItem>? Tenants);
