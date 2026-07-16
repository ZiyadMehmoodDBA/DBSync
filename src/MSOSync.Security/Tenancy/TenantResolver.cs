using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class TenantResolver(
    ITenantAccessValidator validator,
    INodeTenantLookup      nodeLookup) : ITenantResolver
{
    public async Task<ITenantContext> ResolveAsync(HttpContext ctx, CancellationToken ct)
    {
        var user = ctx.User;

        // No authenticated user → 401
        if (user.Identity?.IsAuthenticated != true)
            throw new TenantAccessException(401, "Authentication required");

        var tenantIdClaim = user.FindFirstValue("tenantId");
        var userIdClaim   = user.FindFirstValue("userId");
        var nodeIdClaim   = user.FindFirstValue("nodeId");

        // 1. Platform token — no tenantId claim
        if (string.IsNullOrEmpty(tenantIdClaim))
            return PlatformTenantContext.Instance;

        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            throw new TenantAccessException(401, "Invalid tenantId claim format");

        // 2. Node token — nodeId claim present
        if (!string.IsNullOrEmpty(nodeIdClaim))
        {
            var storedTenantId = await nodeLookup.GetNodeTenantIdAsync(nodeIdClaim, ct);
            if (storedTenantId is null || storedTenantId.Value != tenantId)
                throw new TenantAccessException(403, "Node token tenant mismatch");

            return new TenantContext(tenantId, tenantSlug: "", EditionType.Community, userId: null, roleId: null);
        }

        // 3. User JWT — userId + tenantId claims
        if (!long.TryParse(userIdClaim, out var userId))
            throw new TenantAccessException(401, "Invalid userId claim");

        var validation = await validator.ValidateAsync(tenantId, userId, ct);

        return new TenantContext(
            tenantId:   validation.TenantId,
            tenantSlug: validation.TenantSlug,
            edition:    validation.Edition,
            userId:     userId,
            roleId:     validation.RoleId);
    }
}
