using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MSOSync.Security.Middleware;

public sealed class NodeTokenAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, NodeSecurityService nodeSecurityService)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/sync", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var nodeId = context.Request.Headers["X-Node-Id"].FirstOrDefault();
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

    private static async Task WriteUnauthorizedAsync(HttpContext context, string error)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { error }));
    }
}
