using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Lifecycle")]
public sealed class DrainLifecycleTests(LifecycleFixture fixture)
{
    [Fact]
    public async Task Drain_active_node_returns_204_and_state_becomes_Draining()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "drain-happy");
        var client = await fixture.LifecycleManagerClientAsync();

        var resp = await client.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/drain",
            new { reason = "maintenance window" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Draining");

        // Lifecycle history should have a Draining entry
        var histResp = await client.GetAsync($"api/v1/node-lifecycle/nodes/{nodeId}/history");
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hist = await histResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = hist.GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("toState").GetString() == "Draining");
    }

    [Fact]
    public async Task Resume_drain_returns_204_and_state_becomes_Active()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "drain-resume");
        var client = await fixture.LifecycleManagerClientAsync();

        // Drain first
        (await client.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/drain",
            new { reason = "scheduled maintenance" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Resume from drain
        var resp = await client.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/resume-drain",
            new { reason = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Active");

        var histResp = await client.GetAsync($"api/v1/node-lifecycle/nodes/{nodeId}/history");
        var hist = await histResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = hist.GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("fromState").GetString() == "Draining"
                                 && i.GetProperty("toState").GetString()   == "Active");
    }

    [Fact]
    public async Task Drain_disabled_node_returns_409()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, "drain-409");
        var client = await fixture.LifecycleManagerClientAsync();

        var resp = await client.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/drain",
            new { reason = "invalid" });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Drain_unknown_node_returns_404()
    {
        var client = await fixture.LifecycleManagerClientAsync();
        var resp = await client.PostAsJsonAsync(
            "api/v1/node-lifecycle/nodes/does-not-exist/drain",
            new { reason = "test" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Drain_without_manage_permission_returns_403()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "drain-403");
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/drain",
            new { reason = "should fail" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Transitions_for_draining_node_include_ResumeDrain_and_Decommission()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "drain-transitions");
        var client = await fixture.LifecycleManagerClientAsync();

        // Drain the node first
        (await client.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/drain",
            new { reason = "check transitions" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp = await client.GetAsync($"api/v1/node-lifecycle/nodes/{nodeId}/transitions");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("allowedTransitions")
            .EnumerateArray()
            .Select(a => a.GetProperty("action").GetString())
            .ToList();

        actions.Should().Contain("ResumeDrain");
        actions.Should().Contain("Decommission");
    }
}
