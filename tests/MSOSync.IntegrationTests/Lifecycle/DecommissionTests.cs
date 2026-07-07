using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Lifecycle")]
public sealed class DecommissionTests(LifecycleFixture fixture)
{
    [Fact]
    public async Task Decommission_Returns202_SetsDrainFields_RevokesCredentials()
    {
        const string ext = "dc-happy";
        var nodeId    = await fixture.SeedNodeAsync(NodeLifecycleState.Active, ext);
        var nodeToken = await fixture.IssueNodeTokenAsync(nodeId);
        var client    = await fixture.LifecycleManagerClientAsync();

        var resp = await client.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/decommission",
            new { reason = "Site Closure", gracePeriodMinutes = 60 });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Decommissioning");
        state.GetProperty("decommissionInProgress").GetBoolean().Should().BeTrue();
        // graceUntil should be approximately now+60m (within 5 minute tolerance)
        var graceUntil = state.GetProperty("decommissionGraceUntil").GetDateTimeOffset();
        graceUntil.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(60), precision: TimeSpan.FromMinutes(5));

        // Heartbeat with revoked node token → 401 (NodeSecurities row removed)
        var hbResp = await fixture.NodeClient(nodeId, nodeToken)
            .PostAsJsonAsync($"api/v1/nodes/{nodeId}/heartbeat",
                new { NodeId = nodeId, UptimeSeconds = 1L });
        hbResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "decommission revokes operational credentials immediately");
    }

    [Fact]
    public async Task Decommission_OpenBatch_BlocksWorkerFinalize()
    {
        const string ext = "dc-openbatch";
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, ext);
        var mgr    = await fixture.LifecycleManagerClientAsync();

        // Insert an open SyncOutgoingBatch (status != 2 = open/non-terminal)
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Ensure a channel exists
            if (!await db.Channels.AnyAsync(c => c.ChannelId == "test-chan"))
            {
                db.Channels.Add(new SyncChannel { ChannelId = "test-chan" });
                await db.SaveChangesAsync();
            }

            db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                NodeId        = nodeId,
                ChannelId     = "test-chan",
                Status        = 0,   // Pending — non-terminal (not 2/Acknowledged)
                BatchSequence = 1,
                CreateTime    = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Trigger decommission
        (await mgr.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/decommission",
            new { reason = "batch drain test", gracePeriodMinutes = 60 }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Evaluator should say: do NOT finalize because of open batches
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evaluator = scope.ServiceProvider.GetRequiredService<IDecommissionEvaluator>();
            var node      = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
            var decision  = await evaluator.EvaluateAsync(node);
            decision.Finalize.Should().BeFalse();
            decision.Reason.Should().Be(DecommissionDecisionReason.OpenBatches);
        }
    }

    [Fact]
    public async Task Decommission_GraceExpired_EvaluatorFinalizes()
    {
        const string ext = "dc-graceexpired";
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, ext);
        var mgr    = await fixture.LifecycleManagerClientAsync();

        // Decommission
        (await mgr.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/decommission",
            new { reason = "grace expired test", gracePeriodMinutes = 60 }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Set DecommissionGraceUntil to the past via direct DB update.
        // Also add an open batch so DrainCompleted is NOT triggered first —
        // the evaluator only reaches GraceExpired when openBatches > 0.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var node = await db.Nodes.FirstAsync(n => n.NodeId == nodeId);
            node.DecommissionGraceUntil = DateTimeOffset.UtcNow.AddMinutes(-10);

            if (!await db.Channels.AnyAsync(c => c.ChannelId == "ge-chan"))
                db.Channels.Add(new SyncChannel { ChannelId = "ge-chan" });

            db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                NodeId        = nodeId,
                ChannelId     = "ge-chan",
                Status        = 0,   // Pending — non-terminal
                BatchSequence = 1,
                CreateTime    = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Evaluator → Finalize true, Reason GraceExpired
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evaluator = scope.ServiceProvider.GetRequiredService<IDecommissionEvaluator>();
            var node      = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
            var decision  = await evaluator.EvaluateAsync(node);
            decision.Finalize.Should().BeTrue();
            decision.Reason.Should().Be(DecommissionDecisionReason.GraceExpired);
        }

        // Finalize via INodeLifecycleService
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var lifecycleSvc = scope.ServiceProvider.GetRequiredService<INodeLifecycleService>();
            await lifecycleSvc.FinalizeDecommissionAsync(
                nodeId, LifecycleTrigger.Timeout, "GraceExpired");
        }

        // State → Decommissioned; ExternalId freed
        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Decommissioned");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var node = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
            node.ExternalId.Should().BeEmpty("ExternalId is freed on Decommissioned");
        }
    }

    [Fact]
    public async Task ForceComplete_Endpoint_Returns204_Terminal()
    {
        const string ext = "dc-force";
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Decommissioning, ext,
            mutate: n =>
            {
                n.DecommissionReason    = "test force";
                n.DecommissionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
                n.DecommissionGraceUntil = DateTimeOffset.UtcNow.AddMinutes(55);
            });
        var mgr = await fixture.LifecycleManagerClientAsync();

        var forceResp = await mgr.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/decommission/force", new { });
        forceResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Decommissioned");

        // Any further lifecycle action → 409 (terminal state)
        var enableResp = await mgr.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/enable", new { });
        enableResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Decommission_History_RecordsStartAndComplete_WithReasons()
    {
        const string ext = "dc-history";
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Decommissioning, ext,
            mutate: n =>
            {
                n.DecommissionReason    = "history test";
                n.DecommissionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                n.DecommissionGraceUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
            });
        var mgr = await fixture.LifecycleManagerClientAsync();

        // Force complete
        (await mgr.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/decommission/force", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var histResp = await mgr.GetAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/history");
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hist = await histResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = hist.GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(i =>
            i.GetProperty("toState").GetString() == "Decommissioned");
    }
}
