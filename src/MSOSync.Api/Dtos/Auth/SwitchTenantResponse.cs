namespace MSOSync.Api.Dtos.Auth;

public sealed record SwitchTenantResponse(string Token, Guid TenantId, string TenantSlug);
