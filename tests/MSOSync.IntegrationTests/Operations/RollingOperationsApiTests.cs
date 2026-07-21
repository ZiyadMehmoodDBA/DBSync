using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.IntegrationTests.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

/// <summary>
/// Rolling operation CRUD integration tests. Uses the Lifecycle fixture
/// (provides INodeAuthorizationService, node seeding, and OPERATOR credentials).
/// </summary>
[Collection("Lifecycle")]
public sealed class RollingOperationsApiTests(LifecycleFixture fixture)
{
    private const string Base = "api/v1/operations/rolling";

    private static object MaintenancePayload(string nodeA, string nodeB) => new
    {
        kind                      = "RollingMaintenance",
        nodeIds                   = new[] { nodeA, nodeB },
        waveSize                  = 1,
        gateSoakSeconds           = 0,
        waveAction                = "auto-window",
        windowSeconds             = 60,
        verificationTimeoutSeconds = 30,
    };

    [Fact]
    public async Task Create_rolling_maintenance_returns_201_with_operation_id()
    {
        var (a, b) = await SeedTwoActiveNodesAsync("cre");
        var client = await fixture.LifecycleManagerClientAsync();

        var resp = await client.PostAsJsonAsync(Base, MaintenancePayload(a, b));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("operationId").GetString();
        id.Should().NotBeNullOrEmpty();
        resp.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_rolling_operation_returns_policy_and_steps()
    {
        var (a, b) = await SeedTwoActiveNodesAsync("get");
        var client = await fixture.LifecycleManagerClientAsync();

        var createResp = await client.PostAsJsonAsync(Base, MaintenancePayload(a, b));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = createBody.GetProperty("operationId").GetString()!;

        var getResp = await client.GetAsync($"{Base}/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("operationId").GetString().Should().Be(id);
        body.GetProperty("steps").GetArrayLength().Should().Be(2);

        var policy = body.GetProperty("policy");
        policy.GetProperty("waveSize").GetInt32().Should().Be(1);
        policy.GetProperty("waveAction").GetString().Should().Be("auto-window");
    }

    [Fact]
    public async Task Create_with_invalid_kind_returns_400()
    {
        var (a, _) = await SeedTwoActiveNodesAsync("bad-kind");
        var client = await fixture.LifecycleManagerClientAsync();

        var resp = await client.PostAsJsonAsync(Base, new
        {
            kind                      = "Nonsense",
            nodeIds                   = new[] { a },
            waveSize                  = 1,
            gateSoakSeconds           = 0,
            waveAction                = "manual-confirm",
            verificationTimeoutSeconds = 30,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_with_non_active_node_returns_409()
    {
        var disabled = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, "roll-disabled");
        var (a, _) = await SeedTwoActiveNodesAsync("roll-noactive");
        var client = await fixture.LifecycleManagerClientAsync();

        var resp = await client.PostAsJsonAsync(Base, new
        {
            kind                      = "RollingMaintenance",
            nodeIds                   = new[] { a, disabled },
            waveSize                  = 1,
            gateSoakSeconds           = 0,
            waveAction                = "manual-confirm",
            verificationTimeoutSeconds = 30,
        });

        // RollingOperationService throws OperationStateException → 409
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pause_pending_operation_returns_409()
    {
        var (a, b) = await SeedTwoActiveNodesAsync("pause");
        var client = await fixture.LifecycleManagerClientAsync();

        var createResp = await client.PostAsJsonAsync(Base, MaintenancePayload(a, b));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("operationId").GetString()!;

        // Newly created op is Pending (worker not running in test host)
        var pauseResp = await client.PostAsJsonAsync($"{Base}/{id}/pause", new { });
        pauseResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Abort_operation_returns_204_and_status_cancelled()
    {
        var (a, b) = await SeedTwoActiveNodesAsync("abort");
        var client = await fixture.LifecycleManagerClientAsync();

        var createResp = await client.PostAsJsonAsync(Base, MaintenancePayload(a, b));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("operationId").GetString()!;

        var abortResp = await client.PostAsJsonAsync($"{Base}/{id}/abort", new { });
        abortResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await client.GetAsync($"{Base}/{id}");
        var body = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Cancelled");

        var steps = body.GetProperty("steps").EnumerateArray().ToList();
        steps.Should().OnlyContain(s => s.GetProperty("status").GetString() == "Skipped");
    }

    [Fact]
    public async Task Rolling_endpoints_without_permission_return_403()
    {
        var (a, b) = await SeedTwoActiveNodesAsync("authz");
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(Base, MaintenancePayload(a, b));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(string a, string b)> SeedTwoActiveNodesAsync(string prefix)
    {
        var a = await fixture.SeedNodeAsync(NodeLifecycleState.Active, $"rol-{prefix}-a");
        var b = await fixture.SeedNodeAsync(NodeLifecycleState.Active, $"rol-{prefix}-b");
        return (a, b);
    }
}
