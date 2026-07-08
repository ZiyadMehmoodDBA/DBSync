// tests/MSOSync.IntegrationTests/Configuration/ConfigurationCycleTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.IntegrationTests.Configuration;

[Collection("Configuration")]
public sealed class ConfigurationCycleTests(ConfigurationFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = fx.CreateClient();
        var token  = await fx.GetJwtAsync(client, fx.AdminUsername, fx.AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task FullCycle_CreateDraft_Publish_Assign_Heartbeat_Current()
    {
        var admin          = await AdminClientAsync();
        var (nodeId, tok)  = await fx.CreateTestNodeAsync();
        var node           = fx.NodeClientFor(nodeId, tok);

        // 1. Create template
        var createResp = await admin.PostAsJsonAsync("/api/v1/configuration/templates",
            new { name = $"cycle-{Guid.NewGuid():N}", description = "Full cycle test",
                  initialSettings = new { heartbeatIntervalSeconds = 30, batchSizeLimit = 100 } });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<TemplateApiDto>(JsonOpts)
            ?? throw new InvalidOperationException("null create response");

        // 2. Publish draft
        (await admin.PostAsync($"/api/v1/configuration/templates/{created.Id}/publish", null))
            .EnsureSuccessStatusCode();

        // 3. Assign v1 to node
        (await admin.PostAsJsonAsync($"/api/v1/configuration/nodes/{nodeId}/assign",
            new { templateId = created.Id, version = 1 }))
            .EnsureSuccessStatusCode();

        // 4. Node heartbeat without applied version → UpdateAvailable
        var hbResp = await node.PostAsJsonAsync(
            $"/api/v1/nodes/{nodeId}/heartbeat",
            new { nodeId, uptimeSeconds = 100L });
        hbResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hbBody = await hbResp.Content.ReadFromJsonAsync<HeartbeatResponseDto>(JsonOpts);
        hbBody.Should().NotBeNull();
        hbBody!.AssignedTemplateVersion.Should().Be(1);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n  = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
            n.ConfigurationState.Should().Be(ConfigurationState.UpdateAvailable);
        }

        // 5. GET /configurations/current → 200 + ETag
        var currentResp = await node.GetAsync("/api/v1/configurations/current");
        currentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = currentResp.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrEmpty("current endpoint must return ETag");

        // 6. Repeat with If-None-Match → 304
        var noneMatchReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/configurations/current");
        noneMatchReq.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!));
        var notModifiedResp = await node.SendAsync(noneMatchReq);
        notModifiedResp.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // 7. Heartbeat with applied version + matching hash → Current
        var currentBody = await currentResp.Content.ReadFromJsonAsync<CurrentConfigApiDto>(JsonOpts);
        currentBody.Should().NotBeNull();
        var hb2Resp = await node.PostAsJsonAsync(
            $"/api/v1/nodes/{nodeId}/heartbeat",
            new
            {
                nodeId,
                uptimeSeconds            = 200L,
                appliedTemplateVersion   = 1,
                appliedEffectiveHash     = currentBody!.ContentHash,
                configurationApplyStatus = "Applied",
            });
        hb2Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n  = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == nodeId);
            n.ConfigurationState.Should().Be(ConfigurationState.Current);
        }
    }

    [Fact]
    public async Task NoTemplate_GET_CurrentConfig_Returns204()
    {
        var hasher = new BCryptPasswordHasher();
        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Nodes.AnyAsync(n => n.NodeId == "unassigned-cycle-node"))
                db.Nodes.Add(new SyncNode
                {
                    NodeId         = "unassigned-cycle-node",
                    GroupId        = "cfg-group",
                    SyncUrl        = "http://unassigned.test",
                    LifecycleState = NodeLifecycleState.Active,
                });
            if (!await db.NodeSecurities.AnyAsync(s => s.NodeId == "unassigned-cycle-node"))
                db.NodeSecurities.Add(new SyncNodeSecurity
                {
                    NodeId           = "unassigned-cycle-node",
                    CurrentTokenHash = hasher.Hash("unassigned-raw-token"),
                    CreatedTime      = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var client = fx.CreateClient();
        client.DefaultRequestHeaders.Add("X-Node-Id",    "unassigned-cycle-node");
        client.DefaultRequestHeaders.Add("X-Node-Token", "unassigned-raw-token");

        var resp = await client.GetAsync("/api/v1/configurations/current");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DuplicateHeartbeat_SameState_NoNewHistoryEvent()
    {
        var admin         = await AdminClientAsync();
        var (nodeId, tok) = await fx.CreateTestNodeAsync();
        var node          = fx.NodeClientFor(nodeId, tok);

        var createResp = await admin.PostAsJsonAsync("/api/v1/configuration/templates",
            new { name = $"dedup-{Guid.NewGuid():N}", description = "Dedup test",
                  initialSettings = new { heartbeatIntervalSeconds = 30, batchSizeLimit = 100 } });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<TemplateApiDto>(JsonOpts);
        (await admin.PostAsync($"/api/v1/configuration/templates/{created!.Id}/publish", null))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/configuration/nodes/{nodeId}/assign",
            new { templateId = created.Id, version = 1 }))
            .EnsureSuccessStatusCode();

        var currentBody = await (await node.GetAsync("/api/v1/configurations/current"))
            .Content.ReadFromJsonAsync<CurrentConfigApiDto>(JsonOpts);

        var hbPayload = new
        {
            nodeId,
            uptimeSeconds            = 300L,
            appliedTemplateVersion   = 1,
            appliedEffectiveHash     = currentBody!.ContentHash,
            configurationApplyStatus = "Applied",
        };

        // First heartbeat to reach Current
        await node.PostAsJsonAsync($"/api/v1/nodes/{nodeId}/heartbeat", hbPayload);

        int historyCountBefore;
        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            historyCountBefore = await db.NodeConfigurationHistories.CountAsync(h => h.NodeId == nodeId);
        }

        // Second identical heartbeat — same state → no new history event
        await node.PostAsJsonAsync($"/api/v1/nodes/{nodeId}/heartbeat", hbPayload);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var historyCountAfter = await db.NodeConfigurationHistories.CountAsync(h => h.NodeId == nodeId);
            historyCountAfter.Should().Be(historyCountBefore,
                "consecutive identical heartbeats must not produce duplicate history events");
        }
    }

    // Local DTOs for deserializing API responses
    private sealed record TemplateApiDto(Guid Id, string Name, string Status);
    private sealed record HeartbeatResponseDto(int? AssignedTemplateVersion, string? ContentHash, string? ConfigurationState);
    private sealed record CurrentConfigApiDto(string ContentHash, object? Effective);
}
