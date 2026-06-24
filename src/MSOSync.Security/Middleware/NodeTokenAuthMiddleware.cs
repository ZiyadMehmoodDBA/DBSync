using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MSOSync.Security.Middleware;

public sealed class NodeTokenAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, NodeSecurityService nodeSecurityService)
    {
        if (!IsNodeTokenProtectedPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var nodeId    = context.Request.Headers["X-Node-Id"].FirstOrDefault();
        var nodeToken = context.Request.Headers["X-Node-Token"].FirstOrDefault();

        if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(nodeToken))
        {
            await WriteUnauthorizedAsync(context, "Invalid node token");
            return;
        }

        var valid = await nodeSecurityService.ValidateTokenAsync(nodeId, nodeToken, context.RequestAborted);
        if (!valid)
        {
            await WriteUnauthorizedAsync(context, "Invalid node token");
            return;
        }

        var identity = new ClaimsIdentity(
            [new Claim("nodeId", nodeId)],
            authenticationType: "NodeToken");

        context.User = new ClaimsPrincipal(identity);
        await next(context);
    }

    private static bool IsNodeTokenProtectedPath(PathString path)
    {
        // Ping is a liveness check — no auth required so hubs can probe children
        if (path.Equals("/api/v1/sync/ping", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.StartsWithSegments("/api/v1/sync", StringComparison.OrdinalIgnoreCase))
            return true;

        // /api/v1/nodes/{nodeId}/heartbeat
        var val = path.Value;
        if (val != null
            && val.StartsWith("/api/v1/nodes/", StringComparison.OrdinalIgnoreCase)
            && val.EndsWith("/heartbeat", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string error)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error }));
    }
}
