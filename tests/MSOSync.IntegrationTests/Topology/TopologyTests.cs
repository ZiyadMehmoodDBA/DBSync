using FluentAssertions;
using MSOSync.Metadata.Topology;
using MSOSync.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MSOSync.IntegrationTests.Topology;

[Collection("Topology")]
public sealed class TopologyTests(TopologyFixture fixture)
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = fixture.CreateClient();
        var token  = await fixture.GetViewerTokenAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetGraph_ReturnsEdgesWithChannelIds()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<TopologyGraphDto>("api/v1/topology/graph");

        result!.Edges.Should().HaveCount(1);
        var edge = result.Edges.Single();
        edge.Id.Should().Be("router:router-hub-store");
        edge.Source.Should().Be("group:group-hub");
        edge.Target.Should().Be("group:group-store");
        edge.IsEnabled.Should().BeTrue();
        edge.ChannelIds.Should().BeEquivalentTo(new[] { "ch-default", "ch-config" });
    }

    [Fact]
    public async Task GetGraph_AggregatesConnectivityCorrectly()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<TopologyGraphDto>("api/v1/topology/graph");

        // hub: 2 nodes (1 Reachable + 1 Degraded) → Degraded; is source of the router
        var hub = result!.Nodes.Single(n => n.GroupId == "group-hub");
        hub.Id.Should().Be("group:group-hub");
        hub.Label.Should().Be("Hub");
        hub.MemberCount.Should().Be(2);
        hub.Status.Should().Be(ConnectivityStatus.Degraded);
        hub.TriggerCount.Should().Be(2);
        hub.ChannelCount.Should().Be(2);

        // store: 1 Reachable node; target-only → no triggers/channels from source perspective
        var store = result.Nodes.Single(n => n.GroupId == "group-store");
        store.MemberCount.Should().Be(1);
        store.Status.Should().Be(ConnectivityStatus.Reachable);
        store.TriggerCount.Should().Be(0);
        store.ChannelCount.Should().Be(0);

        // empty: no nodes
        var empty = result.Nodes.Single(n => n.GroupId == "group-empty");
        empty.MemberCount.Should().Be(0);
        empty.Status.Should().Be(ConnectivityStatus.Unknown);

        // meta
        result.Meta.TotalGroups.Should().Be(3);
        result.Meta.TotalNodes.Should().Be(3);   // hub-1 + hub-2 + store-1
        result.Meta.OnlineNodes.Should().Be(1);  // only group-store is Reachable
    }

    [Fact]
    public async Task GetSummary_ReturnsExpectedCounts()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<TopologySummaryDto>("api/v1/topology/summary");

        result!.TotalGroups.Should().Be(3);
        result.TotalNodes.Should().Be(3);
        result.ReachableNodes.Should().Be(2);
        result.DegradedNodes.Should().Be(1);
        result.UnreachableNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetGroups_ReturnsAllGroups()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<IReadOnlyList<TopologyGroupDto>>("api/v1/topology/groups");

        result!.Should().HaveCount(3);
        result!.Select(g => g.GroupId).Should().Contain(
            new[] { "group-hub", "group-store", "group-empty" });
    }

    [Fact]
    public async Task GetGroup_Missing_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/topology/groups/nonexistent-group");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetGroupNodes_ReturnsMembers()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<IReadOnlyList<TopologyGroupNodeDto>>(
            "api/v1/topology/groups/group-hub/nodes");

        result!.Should().HaveCount(2);
        result!.Select(n => n.NodeId).Should().BeEquivalentTo(new[] { "hub-1", "hub-2" });
    }

    [Fact]
    public async Task GetGroupNodes_EmptyGroup_ReturnsEmptyArray()
    {
        var client = await AuthenticatedClientAsync();

        var result = await client.GetFromJsonAsync<IReadOnlyList<TopologyGroupNodeDto>>(
            "api/v1/topology/groups/group-empty/nodes");

        result!.Should().BeEmpty();
    }

    [Fact]
    public async Task Unauthorized_Returns401()
    {
        var client = fixture.CreateClient();

        var resp = await client.GetAsync("api/v1/topology/graph");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
