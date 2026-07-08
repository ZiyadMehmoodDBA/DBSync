// tests/MSOSync.IntegrationTests/Heartbeat/HeartbeatTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Heartbeat;

[Collection("Heartbeat")]
public sealed class HeartbeatTests(HeartbeatFixture fx)
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    private HttpClient NodeClient()
    {
        var client = fx.CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    fx.NodeId);
        client.DefaultRequestHeaders.Add("X-Node-Token", fx.NodeToken);
        return client;
    }

    [Fact]
    public async Task Heartbeat_ValidNodeToken_Returns200()
    {
        var resp = await NodeClient().PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new { NodeId = fx.NodeId, UptimeSeconds = 100L });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_ValidNodeToken_ReturnsHeartbeatResponseBody()
    {
        var resp = await NodeClient().PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new { NodeId = fx.NodeId, UptimeSeconds = 100L });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<HeartbeatResponse>(_jsonOpts);
        body.Should().NotBeNull();
        // ConfigurationState should be present (null = no template assigned is valid)
        // AssignedTemplateId, AssignedTemplateVersion, ContentHash may be null if no template assigned
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
    public async Task Heartbeat_ActiveNode_RecordsHeartbeatAndReturns200()
    {
        // Heartbeat on an Active node records heartbeat and returns 200; lifecycle state is unchanged.
        var resp = await NodeClient().PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new { NodeId = fx.NodeId, UptimeSeconds = 200L });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var node = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == fx.NodeId);
            node.LifecycleState.Should().Be(NodeLifecycleState.Active);
            node.LastHeartbeat.Should().NotBeNull();
        }
    }
}
