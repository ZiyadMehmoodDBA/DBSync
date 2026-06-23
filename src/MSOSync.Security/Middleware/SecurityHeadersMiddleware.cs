using Microsoft.AspNetCore.Http;

namespace MSOSync.Security.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"]  = "nosniff";
        context.Response.Headers["X-Frame-Options"]         = "DENY";
        context.Response.Headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; object-src 'none'";
        await next(context);
    }
}
