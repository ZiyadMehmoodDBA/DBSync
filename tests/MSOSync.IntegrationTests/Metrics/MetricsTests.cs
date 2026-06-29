// tests/MSOSync.IntegrationTests/Metrics/MetricsTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Metrics;

[Collection("Metrics")]
public sealed class MetricsTests(MetricsFixture fixture)
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var token  = await fixture.GetViewerTokenAsync();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetSummary_Returns200_WithGeneratedAt()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/summary");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("generatedAt").GetDateTime().Should()
            .BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        body.GetProperty("totalNodes").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetNodes_Returns200_WithSeededNodes()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/nodes");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var nodes = body.EnumerateArray().ToList();
        nodes.Should().HaveCount(2);

        var hub = nodes.Single(n => n.GetProperty("nodeId").GetString() == "hub-1");
        hub.GetProperty("connectivityStatus").GetInt32().Should().Be(1); // Reachable = 1
    }

    [Fact]
    public async Task GetChannels_Returns200_WithPendingEvents()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/channels");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var channels = body.EnumerateArray().ToList();
        channels.Should().NotBeEmpty();

        var def = channels.Single(c => c.GetProperty("channelId").GetString() == "ch-default");
        def.GetProperty("pendingEvents").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task GetRuntime_Returns200_WithRuntimeStats()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/runtime");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        // Ordered descending by CreateTime — most recent first
        rows[0].GetProperty("heapUsed").GetInt64().Should().Be(600_000_000L);
    }

    [Fact]
    public async Task GetMonitors_Returns200_WithAllRows()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/monitors");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateArray().Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMonitors_FilteredByNodeIdAndMetricName()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/metrics/monitors?nodeId=hub-1&metricName=cpu");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.EnumerateArray().ToList();
        rows.Should().HaveCount(1);
        rows[0].GetProperty("metricValue").GetString().Should().Be("12.5");
    }

    [Fact]
    public async Task AllEndpoints_Unauthorized_Returns401()
    {
        var client = fixture.CreateClient();

        var summaryResp  = await client.GetAsync("api/v1/metrics/summary");
        var nodesResp    = await client.GetAsync("api/v1/metrics/nodes");
        var channelsResp = await client.GetAsync("api/v1/metrics/channels");
        var runtimeResp  = await client.GetAsync("api/v1/metrics/runtime");
        var monitorsResp = await client.GetAsync("api/v1/metrics/monitors");

        summaryResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        nodesResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        channelsResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        runtimeResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        monitorsResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
