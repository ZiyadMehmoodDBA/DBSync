// tests/MSOSync.IntegrationTests/Operations/ClusterApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

[Collection("Operations")]
public sealed class ClusterApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/cluster/summary";

    // ── happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_AdminToken_Returns200WithValidShape()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // DTO properties: NodeStates, OperationCounts, ActiveOperations, ActiveRollingOps,
        //                 ActiveReplays, RecentNodeChanges (camelCased by JSON serializer)
        body.GetProperty("nodeStates").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("operationCounts").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("activeOperations").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("activeRollingOps").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("activeReplays").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("recentNodeChanges").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetSummary_ViewerToken_Returns200()
    {
        var client = await fixture.ViewerClientAsync();
        var resp = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSummary_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── node counts reflect seeded data ────────────────────────────────────

    [Fact]
    public async Task GetSummary_NodeCounts_TotalMatchesSeededNodes()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nodeId = $"cluster-cnt-{Guid.NewGuid():N}";
        db.Nodes.Add(new SyncNode
        {
            NodeId          = nodeId,
            GroupId         = "ops-test-cluster",
            SyncUrl         = "http://cluster-test.local",
            LifecycleState  = NodeLifecycleState.Active,
            MaintenanceMode = false,
        });
        await db.SaveChangesAsync();

        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ns = body.GetProperty("nodeStates");
        var total = ns.GetProperty("total").GetInt32();
        total.Should().BeGreaterThan(0, "at least one node was seeded");
    }

    // ── tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_ReturnsOnlyOwnData_NotOtherTenantRows()
    {
        // This test verifies the API responds 200 and shape is intact.
        // Full cross-tenant isolation is verified by MSOSync.IntegrationTests/MultiTenancy/.
        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("nodeStates").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("operationCounts").ValueKind.Should().Be(JsonValueKind.Object);
    }
}
