using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Dashboard;

[Collection("Dashboard")]
public sealed class DashboardTests(DashboardFixture fixture)
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
    public async Task GetSummary_Returns200_WithCorrectNodeCounts()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/dashboard/summary");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalNodes").GetInt32().Should().Be(3);
        body.GetProperty("reachableNodes").GetInt32().Should().Be(2);
        body.GetProperty("degradedNodes").GetInt32().Should().Be(1);
        body.GetProperty("pendingEvents").GetInt64().Should().Be(2);
        body.GetProperty("queueDepth").GetInt64().Should().Be(1); // 1 pending, 1 acknowledged
        body.GetProperty("transportErrors24h").GetInt64().Should().Be(1);
        body.GetProperty("generatedAt").GetDateTime().Should()
            .BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetActivity_Returns200_WithMixedItems()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/dashboard/activity");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.EnumerateArray().ToList();
        // AuditService writes LOGIN_SUCCESS on every login, so audit rows accumulate beyond the 3 seeded
        items.Count.Should().BeGreaterThanOrEqualTo(4); // at least 3 audit + 1 batch_error
        items.Should().Contain(i => i.GetProperty("type").GetString() == "audit");
        items.Should().Contain(i => i.GetProperty("type").GetString() == "batch_error");
    }

    [Fact]
    public async Task GetActivity_FilterByType_ReturnsOnlyAudit()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/dashboard/activity?type=audit");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.EnumerateArray().ToList();
        // AuditService writes LOGIN_SUCCESS on every login, so >= 3 audit rows
        items.Count.Should().BeGreaterThanOrEqualTo(3);
        items.Should().AllSatisfy(i =>
            i.GetProperty("type").GetString().Should().Be("audit"));
    }

    [Fact]
    public async Task GetActivity_InvalidType_Returns400()
    {
        var client = await AuthenticatedClientAsync();

        var resp = await client.GetAsync("api/v1/dashboard/activity?type=invalid");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSummary_Unauthenticated_Returns401()
    {
        var client = fixture.CreateClient();

        var resp = await client.GetAsync("api/v1/dashboard/summary");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
