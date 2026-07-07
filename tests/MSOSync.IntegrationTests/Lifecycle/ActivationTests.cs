using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Lifecycle")]
public sealed class ActivationTests(LifecycleFixture fixture)
{
    private static object Body(string externalId, string token) =>
        new { externalId, bootstrapToken = token, agentVersion = "1.0.0" };

    [Fact]
    public async Task Activate_PendingRegistration_Returns200_TokenAndIntervals_NodeBecomesActive()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.PendingRegistration, "act-happy");
        var token  = await fixture.IssueBootstrapTokenAsync(nodeId);

        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/nodes/activate", Body("act-happy", token));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("nodeToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("heartbeatIntervalSeconds").GetInt32().Should().Be(30);
        body.GetProperty("probeIntervalSeconds").GetInt32().Should().Be(60);
        body.GetProperty("configurationVersion").GetInt32().Should().Be(1);

        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Activate_ConsumedTokenReplay_DeniesReactivation()   // retry safety
    {
        // After successful activation the token is consumed. A replay attempt on the now-Active
        // node returns 401 because ValidateAndConsumeAsync runs BEFORE the state machine check
        // (spec §4.5): a consumed token is always unauthorized, regardless of current state.
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.PendingRegistration, "act-replay");
        var token  = await fixture.IssueBootstrapTokenAsync(nodeId);
        var anon   = fixture.AnonymousClient();

        (await anon.PostAsJsonAsync("api/v1/nodes/activate", Body("act-replay", token)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var replayResp = await anon.PostAsJsonAsync("api/v1/nodes/activate", Body("act-replay", token));
        // Token was consumed on first activation; replay must return 401 (token-before-state order).
        replayResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activate_RevokedToken_Returns401()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.PendingRegistration, "act-revoked");
        var token  = await fixture.IssueBootstrapTokenAsync(nodeId);
        await fixture.RevokeBootstrapTokensAsync(nodeId);

        (await fixture.AnonymousClient().PostAsJsonAsync("api/v1/nodes/activate", Body("act-revoked", token)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activate_WrongState_Disabled_Returns409()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, "act-wrongstate");
        var token  = await fixture.IssueBootstrapTokenAsync(nodeId);

        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/nodes/activate", Body("act-wrongstate", token));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_LIFECYCLE_TRANSITION");
        body.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Activate_UnknownExternalId_Returns401_NotFoundNotLeaked()
        => (await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/nodes/activate", Body("no-such-node", "whatever")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
