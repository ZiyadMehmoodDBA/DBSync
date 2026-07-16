using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

// Registered as Singleton. Reads the current request's ITenantContext at EF query time.
// This bridges the EF Core model-cache boundary — the Singleton reference is stable,
// but TenantId is evaluated fresh per query from the current request scope.
public sealed class HttpContextCurrentTenantAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentTenantAccessor
{
    public Guid? TenantId
    {
        get
        {
            var holder = httpContextAccessor.HttpContext?
                .RequestServices?
                .GetService<TenantContextHolder>();

            var ctx = holder?.Context;
            return ctx is { IsPlatformContext: false } ? ctx.TenantId : null;
        }
    }
}
