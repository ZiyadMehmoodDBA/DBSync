// Two explicit stages (spec §10):
//   1. Permission validation — this service (one implementation instead of nine copies).
//   2. Business rule validation — the state machine + NodeLifecycleService commands
//      (cannot enable a Rejected node, cannot decommission a terminal node, ...).
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Authorization;

public interface INodeAuthorizationService
{
    /// Throws ForbiddenOperationException (→ 403 via GlobalExceptionHandler) when the
    /// current user lacks the permission.
    Task EnsurePermissionAsync(string permissionKey, CancellationToken ct);
}

public sealed class NodeAuthorizationService(
    IPermissionService permissionService,
    ICurrentUserService currentUser) : INodeAuthorizationService
{
    public async Task EnsurePermissionAsync(string permissionKey, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(permissionKey))
            throw new ForbiddenOperationException(
                $"Missing permission {permissionKey}", "FORBIDDEN");
    }
}
