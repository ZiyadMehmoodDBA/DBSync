using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

/// <summary>
/// Heartbeat accept/reject matrix per spec §5.3.
/// Each test seeds its own node with a unique externalId to ensure isolation.
/// For Decommissioning we seed directly with a node token (not triggered via API)
/// because the decommission API revokes credentials at start — as per spec.
/// </summary>
[Collection("Lifecycle")]
public sealed class HeartbeatLifecycleTests(LifecycleFixture fixture)
{
    private static object Hb(string nodeId) => new { NodeId = nodeId, UptimeSeconds = 1L };

    [Fact]
    public async Task Heartbeat_Active_Returns200()
    {
        var nodeId    = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "hb-active");
        var token     = await fixture.IssueNodeTokenAsync(nodeId);
        var resp = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_Recovery_Returns200()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Recovery, "hb-recovery",
            mutate: n => n.PreviousLifecycleState = NodeLifecycleState.Active);
        var token  = await fixture.IssueNodeTokenAsync(nodeId);
        var resp   = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_Decommissioning_Returns200()
    {
        // Seed Decommissioning directly and issue a fresh token — decommission API would revoke it.
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Decommissioning, "hb-decommissioning",
            mutate: n =>
            {
                n.DecommissionReason     = "hb test";
                n.DecommissionStartedAt  = DateTimeOffset.UtcNow.AddMinutes(-1);
                n.DecommissionGraceUntil = DateTimeOffset.UtcNow.AddMinutes(59);
            });
        var token = await fixture.IssueNodeTokenAsync(nodeId);
        var resp  = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_PendingRegistration_Returns403()
    {
        // No operational token issued — send a fake token; auth passes via a fresh token issued here,
        // but the lifecycle gate then returns 403.
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.PendingRegistration, "hb-pending");
        var token  = await fixture.IssueNodeTokenAsync(nodeId);
        var resp   = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heartbeat_Disabled_Returns403()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, "hb-disabled");
        var token  = await fixture.IssueNodeTokenAsync(nodeId);
        var resp   = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heartbeat_Decommissioned_Returns410()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Decommissioned, "hb-decommissioned");
        var token  = await fixture.IssueNodeTokenAsync(nodeId);
        var resp   = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Heartbeat_Rejected_Returns410()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Rejected, "hb-rejected");
        var token  = await fixture.IssueNodeTokenAsync(nodeId);
        var resp   = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Heartbeat_Active_NeverWritesLifecycle()
    {
        // Heartbeat on Active node → state unchanged, no new lifecycle history row added
        var nodeId    = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "hb-nolc");
        var token     = await fixture.IssueNodeTokenAsync(nodeId);

        // Count history rows before
        int countBefore;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            countBefore = await db.NodeLifecycleHistories
                .CountAsync(h => h.NodeId == nodeId);
        }

        var resp = await fixture.NodeClient(nodeId, token)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat", Hb(nodeId));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Count after — must be same
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var countAfter = await db.NodeLifecycleHistories
                .CountAsync(h => h.NodeId == nodeId);
            countAfter.Should().Be(countBefore,
                "heartbeat must never write a lifecycle history row");
        }
    }
}
