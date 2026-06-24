// tests/MSOSync.IntegrationTests/Heartbeat/HeartbeatTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Heartbeat;

[Collection("Heartbeat")]
public sealed class HeartbeatTests(HeartbeatFixture fx)
{
    private HttpClient NodeClient()
    {
        var client = fx.CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    fx.NodeId);
        client.DefaultRequestHeaders.Add("X-Node-Token", fx.NodeToken);
        return client;
    }

    [Fact]
    public async Task Heartbeat_ValidNodeToken_Returns204()
    {
        var resp = await NodeClient().PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new { NodeId = fx.NodeId, UptimeSeconds = 100L });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Heartbeat_NodeIdMismatch_Returns400()
    {
        var resp = await NodeClient().PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new { NodeId = "different-node", UptimeSeconds = 100L });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Heartbeat_NoToken_Returns401()
    {
        var client = fx.CreateClient();
        var resp   = await client.PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new { NodeId = fx.NodeId, UptimeSeconds = 100L });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Heartbeat_OfflineNode_RecoveredToRegistered()
    {
        // Force node to OFFLINE
        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Nodes
                .Where(n => n.NodeId == fx.NodeId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, "OFFLINE"));
        }

        var resp = await NodeClient().PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new { NodeId = fx.NodeId, UptimeSeconds = 200L });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var node = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == fx.NodeId);
            node.Status.Should().Be("REGISTERED");
        }
    }
}
