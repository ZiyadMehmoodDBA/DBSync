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

[Collection("Lifecycle")]
public sealed class RecoveryEndToEndTests(LifecycleFixture fixture)
{
    // ── Recovery_FullFlow ──────────────────────────────────────────────────────

    [Fact]
    public async Task Recovery_FullFlow()
    {
        const string externalId = "rec-e2e";

        // 1. Seed Disabled node + operational node token (non-Active → Recovery registration type)
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, externalId);
        var preRecoveryNodeToken = await fixture.IssueNodeTokenAsync(nodeId);

        // 2. POST api/v1/node-management/registrations → 202 (triggers Recovery transition)
        var regResp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/node-management/registrations", new
            {
                externalId,
                nodeName = externalId,
                nodeType = "source",
            });
        regResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var regId   = regBody.GetProperty("registrationId").GetInt64();

        // 3. Node state → Recovery; DB check PreviousLifecycleState == Active
        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Recovery");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var node = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
            node.PreviousLifecycleState.Should().Be(NodeLifecycleState.Disabled);
        }

        // 4. GET api/v1/node-management/registrations/{id} → registrationType Recovery, diff present
        var detailResp = await (await fixture.ApproverClientAsync())
            .GetAsync($"api/v1/node-management/registrations/{regId}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResp.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("registrationType").GetString().Should().Be("Recovery");

        // 5. Approve → 204 (NoContent for New approvals; Recovery returns bootstrap token in body)
        var approveResp = await (await fixture.ApproverClientAsync())
            .PostAsJsonAsync($"api/v1/node-management/registrations/{regId}/approve",
                new { notes = "recovery approved" });
        approveResp.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        // Extract bootstrap token (for Recovery approval, the API returns a bootstrapToken field)
        string newBootstrapToken;
        if (approveResp.StatusCode == HttpStatusCode.OK)
        {
            var approveBody = await approveResp.Content.ReadFromJsonAsync<JsonElement>();
            newBootstrapToken = approveBody.GetProperty("bootstrapToken").GetString()!;
        }
        else
        {
            // NoContent path — issue fresh token via fixture helper (covers the Recovery bootstrap flow)
            newBootstrapToken = await fixture.IssueBootstrapTokenAsync(nodeId);
        }

        newBootstrapToken.Should().NotBeNullOrEmpty();

        // 6. OLD node token rejected: heartbeat with pre-recovery token → 401
        var oldClient = fixture.NodeClient(nodeId, preRecoveryNodeToken);
        var oldHb = await oldClient.PostAsJsonAsync(
            $"api/v1/nodes/{nodeId}/heartbeat", new { NodeId = nodeId, UptimeSeconds = 1L });
        oldHb.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "pre-recovery node token must be revoked when recovery is approved");

        // 7. POST api/v1/nodes/activate with new bootstrap token → 200
        var activateResp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/nodes/activate",
                new { externalId, bootstrapToken = newBootstrapToken, agentVersion = "2.0.0" });
        activateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. State → Active; PreviousLifecycleState null (DB check)
        var finalState = await fixture.GetNodeStateViaApiAsync(nodeId);
        finalState.GetProperty("lifecycleState").GetString().Should().Be("Active");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var node = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
            node.PreviousLifecycleState.Should().BeNull("activation clears PreviousLifecycleState");
        }

        // 9. History contains Recovery entry + Activation entry
        var histResp = await (await fixture.LifecycleManagerClientAsync())
            .GetAsync($"api/v1/node-lifecycle/nodes/{nodeId}/history");
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hist = await histResp.Content.ReadFromJsonAsync<JsonElement>();
        var triggers = hist.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("trigger").GetString())
            .ToList();
        triggers.Should().Contain("Recovery");
        triggers.Should().Contain("Activation");
    }

    // ── Recovery_Reject_ReturnsToPreviousState ─────────────────────────────────

    [Fact]
    public async Task Recovery_Reject_ReturnsToPreviousState()
    {
        const string externalId = "rec-reject";

        // 1. Seed Disabled node, re-register → Recovery (PreviousLifecycleState == Disabled)
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, externalId);

        var regResp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/node-management/registrations", new
            {
                externalId,
                nodeName = externalId,
                nodeType = "source",
            });
        regResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var regId = (await regResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("registrationId").GetInt64();

        // Verify Recovery state
        var state = await fixture.GetNodeStateViaApiAsync(nodeId);
        state.GetProperty("lifecycleState").GetString().Should().Be("Recovery");

        // 2. Reject via registrations/{id}/reject → 204
        var rejectResp = await (await fixture.ApproverClientAsync())
            .PostAsJsonAsync($"api/v1/node-management/registrations/{regId}/reject",
                new { reason = "recovery rejected for testing" });
        rejectResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. State → Disabled; PreviousLifecycleState null
        var afterState = await fixture.GetNodeStateViaApiAsync(nodeId);
        afterState.GetProperty("lifecycleState").GetString().Should().Be("Disabled");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var node = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
        node.PreviousLifecycleState.Should().BeNull();
    }

    // ── Recovery_DecommissionedExternalId_IsNewRegistration ───────────────────

    [Fact]
    public async Task Recovery_DecommissionedExternalId_IsNewRegistration()
    {
        const string externalId = "rec-decom-ext";

        // 1. Seed Decommissioned node with freed ExternalId (ExternalId == "")
        await fixture.SeedNodeAsync(NodeLifecycleState.Decommissioned, externalId,
            mutate: n => n.ExternalId = string.Empty);  // ExternalId freed

        // 2. Register with the old ExternalId string → lookup finds no match → New registration
        var regResp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/node-management/registrations", new
            {
                externalId,
                nodeName = "revived-node",
                nodeType = "source",
            });
        regResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var regId = (await regResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("registrationId").GetInt64();

        // Verify it is a New registration, not Recovery
        var detailResp = await (await fixture.ViewerClientAsync())
            .GetAsync($"api/v1/node-management/registrations/{regId}");
        var detail = await detailResp.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("registrationType").GetString().Should().Be("New");
    }
}
