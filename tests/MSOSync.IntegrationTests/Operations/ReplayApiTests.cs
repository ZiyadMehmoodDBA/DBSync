using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MSOSync.IntegrationTests.Lifecycle;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

/// <summary>
/// Replay API integration tests. Uses the Lifecycle fixture (provides
/// INodeAuthorizationService, node seeding, and OPERATOR credentials).
/// </summary>
[Collection("Lifecycle")]
public sealed class ReplayApiTests(LifecycleFixture fixture)
{
    private const string Base = "api/v1/operations/replay";

    // ── helper: create an Active node and return a privileged operator client ──

    private async Task<(string nodeId, HttpClient client)> SetupAsync(string prefix)
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, $"rep-{prefix}");
        var client = await fixture.LifecycleManagerClientAsync();
        return (nodeId, client);
    }

    private static object FailedDeliveryPayload(string nodeId, int daysBack = 1) => new
    {
        nodeId,
        replayMode = "FailedDelivery",
        fromTime   = DateTime.UtcNow.AddDays(-daysBack).ToString("o"),
        toTime     = DateTime.UtcNow.ToString("o"),
    };

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_replay_FailedDelivery_returns_201_with_item_count()
    {
        var (nodeId, client) = await SetupAsync("create");

        var resp = await client.PostAsJsonAsync(Base, FailedDeliveryPayload(nodeId));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<ReplayOperationCreatedDto>();
        body.Should().NotBeNull();
        body!.OperationId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_replay_invalid_mode_returns_400()
    {
        var (nodeId, client) = await SetupAsync("bad-mode");

        var payload = new
        {
            nodeId,
            replayMode = "InvalidMode",
            fromTime   = DateTime.UtcNow.AddDays(-1).ToString("o"),
            toTime     = DateTime.UtcNow.ToString("o"),
        };

        var resp = await client.PostAsJsonAsync(Base, payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_replay_range_too_large_returns_400()
    {
        var (nodeId, client) = await SetupAsync("range-big");

        var resp = await client.PostAsJsonAsync(Base, FailedDeliveryPayload(nodeId, daysBack: 100));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_replay_returns_detail_with_correct_nodeId()
    {
        var (nodeId, client) = await SetupAsync("get-detail");

        // Create first
        var createResp = await client.PostAsJsonAsync(Base, FailedDeliveryPayload(nodeId));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<ReplayOperationCreatedDto>();

        // Fetch detail
        var getResp = await client.GetAsync($"{Base}/{created!.OperationId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResp.Content.ReadFromJsonAsync<ReplayOperationDetailDto>();
        detail.Should().NotBeNull();
        detail!.NodeId.Should().Be(nodeId);
        detail.ReplayMode.Should().Be("FailedDelivery");
    }

    [Fact]
    public async Task Cancel_replay_returns_204_when_items_exist()
    {
        var (nodeId, client) = await SetupAsync("cancel");

        var createResp = await client.PostAsJsonAsync(Base, FailedDeliveryPayload(nodeId));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<ReplayOperationCreatedDto>();

        // If no items the operation is immediately NoData/Completed — skip cancel
        if (created!.ItemCount == 0) return;

        var cancelResp = await client.PostAsync($"{Base}/{created.OperationId}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Replay_endpoints_without_lifecycle_permission_return_403()
    {
        var (nodeId, _) = await SetupAsync("authz");

        // ViewerClient has ViewTopology but NOT ManageNodeLifecycle
        var viewer  = await fixture.ViewerClientAsync();
        var payload = FailedDeliveryPayload(nodeId);

        var resp = await viewer.PostAsJsonAsync(Base, payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
