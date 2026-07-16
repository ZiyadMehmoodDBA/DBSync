using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class TenantResolverMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ITenantResolver resolver)
    {
        // Skip tenant resolution for unauthenticated requests (e.g., login endpoint)
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            await next(ctx);
            return;
        }

        try
        {
            var tenantContext = await resolver.ResolveAsync(ctx, ctx.RequestAborted);
            // Register resolved context as scoped so controllers + services + DbContext can inject it
            ctx.RequestServices.GetRequiredService<TenantContextHolder>().Context = tenantContext;
            ctx.Items["IsPlatformContext"] = tenantContext.IsPlatformContext;
        }
        catch (TenantAccessException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
            return;
        }

        await next(ctx);
    }
}

// Scoped holder so DbContext (and any scoped service) can get the resolved context via DI
public sealed class TenantContextHolder
{
    public ITenantContext? Context { get; set; }
}
