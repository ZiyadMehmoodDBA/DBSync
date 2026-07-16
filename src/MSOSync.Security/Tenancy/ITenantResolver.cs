using MSOSync.Common.Tenancy;
using Microsoft.AspNetCore.Http;

namespace MSOSync.Security.Tenancy;

public interface INodeTenantLookup
{
    Task<Guid?> GetNodeTenantIdAsync(string nodeId, CancellationToken ct);
}

public interface ITenantResolver
{
    Task<ITenantContext> ResolveAsync(HttpContext ctx, CancellationToken ct);
}
