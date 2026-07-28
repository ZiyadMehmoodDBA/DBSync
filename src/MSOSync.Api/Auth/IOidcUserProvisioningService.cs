using System.Security.Claims;
using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

public interface IOidcUserProvisioningService
{
    Task<SyncUser> ProvisionAsync(ClaimsPrincipal principal, string providerName, CancellationToken ct = default);
}
