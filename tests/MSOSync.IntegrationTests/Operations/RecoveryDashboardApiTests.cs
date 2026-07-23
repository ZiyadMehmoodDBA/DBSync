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
public sealed class RecoveryDashboardApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/cluster/recovery";

    [Fact]
    public async Task GetRecovery_Returns200WithCorrectShape()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("summary").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("activeRecoveries").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("recentCompletedRecoveries").ValueKind.Should().Be(JsonValueKind.Array);

        var summary = body.GetProperty("summary");
        summary.GetProperty("activeCount").ValueKind.Should().Be(JsonValueKind.Number);
        summary.GetProperty("completedLast30Days").ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task GetRecovery_SeededRecoveryNode_AppearsInActiveRecoveries()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nodeId = $"int-rec-{Guid.NewGuid():N}";
        db.Nodes.Add(new SyncNode
        {
            NodeId         = nodeId,
            GroupId        = "int-test",
            SyncUrl        = "http://int-rec.local",
            LifecycleState = NodeLifecycleState.Recovery,
        });
        db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId     = nodeId,
            FromState  = NodeLifecycleState.Active,
            ToState    = NodeLifecycleState.Recovery,
            Trigger    = LifecycleTrigger.System,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-20),
        });
        await db.SaveChangesAsync();

        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var active = body.GetProperty("activeRecoveries");
        active.GetArrayLength().Should().BeGreaterThan(0);

        var nodeIds = active.EnumerateArray()
            .Select(e => e.GetProperty("nodeId").GetString())
            .ToList();
        nodeIds.Should().Contain(nodeId);
    }

    [Fact]
    public async Task GetRecovery_CompletedRecovery_AppearsInCompleted()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var nodeId        = $"int-done-{Guid.NewGuid():N}";
        var recoveryStart = DateTimeOffset.UtcNow.AddDays(-3);
        var restored      = recoveryStart.AddMinutes(30);

        db.Nodes.Add(new SyncNode { NodeId = nodeId, GroupId = "int-test", SyncUrl = "http://int-done.local", LifecycleState = NodeLifecycleState.Active });
        db.NodeLifecycleHistories.AddRange(
            new SyncNodeLifecycleHistory { NodeId = nodeId, FromState = NodeLifecycleState.Active,   ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, OccurredAt = recoveryStart },
            new SyncNodeLifecycleHistory { NodeId = nodeId, FromState = NodeLifecycleState.Recovery, ToState = NodeLifecycleState.Active,   Trigger = LifecycleTrigger.Manual, OccurredAt = restored      });
        await db.SaveChangesAsync();

        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body      = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var completed = body.GetProperty("recentCompletedRecoveries");
        var nodeIds   = completed.EnumerateArray()
            .Select(e => e.GetProperty("nodeId").GetString())
            .ToList();
        nodeIds.Should().Contain(nodeId);
    }

    [Fact]
    public async Task GetRecovery_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
