// tests/MSOSync.IntegrationTests/Configuration/ConfigurationPerformanceTests.cs
using System.Diagnostics;
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
public sealed class ConfigurationPerformanceTests(ConfigurationFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = fx.CreateClient();
        var token  = await fx.GetJwtAsync(client, fx.AdminUsername, fx.AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> CreateAndPublishTemplateAsync(HttpClient admin)
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/configuration/templates",
            new { name = $"perf-tmpl-{Guid.NewGuid():N}", description = "Perf test",
                  initialSettings = new { heartbeatIntervalSeconds = 30, batchSizeLimit = 100 } });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<TemplateApiDto>(JsonOpts);
        (await admin.PostAsync($"/api/v1/configuration/templates/{created!.Id}/publish", null))
            .EnsureSuccessStatusCode();
        return created.Id;
    }

    [Fact]
    public async Task Rollout_50Nodes_CompletesWithin_15s()
    {
        var admin      = await AdminClientAsync();
        var templateId = await CreateAndPublishTemplateAsync(admin);

        var hasher   = new BCryptPasswordHasher();
        var nodeIds  = Enumerable.Range(1, 50).Select(i => $"perf-node-{i:000}").ToList();
        var rawToken = "perf-raw-token-fixed";

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var nid in nodeIds)
            {
                if (!await db.Nodes.AnyAsync(n => n.NodeId == nid))
                    db.Nodes.Add(new SyncNode
                    {
                        NodeId         = nid,
                        GroupId        = "cfg-group",
                        SyncUrl        = $"http://{nid}.test",
                        LifecycleState = NodeLifecycleState.Active,
                    });
                if (!await db.NodeSecurities.AnyAsync(s => s.NodeId == nid))
                    db.NodeSecurities.Add(new SyncNodeSecurity
                    {
                        NodeId           = nid,
                        CurrentTokenHash = hasher.Hash(rawToken),
                        CreatedTime      = DateTime.UtcNow,
                    });
            }
            await db.SaveChangesAsync();
        }

        var sw = Stopwatch.StartNew();
        var rolloutResp = await admin.PostAsJsonAsync("/api/v1/configuration/rollout", new
        {
            nodeIds    = nodeIds,
            templateId = templateId,
            templateVersion = 1,
        });
        rolloutResp.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "rollout of 50 nodes must be accepted");

        var rolloutBody = await rolloutResp.Content.ReadFromJsonAsync<RolloutAcceptedDto>(JsonOpts)!;
        var rolloutId   = rolloutBody!.RolloutId;

        string? status = null;
        while (status is not "Completed" and not "Failed" && sw.Elapsed.TotalSeconds < 15)
        {
            await Task.Delay(300);
            var statusResp = await admin.GetAsync($"/api/v1/configuration/rollout/{rolloutId}");
            if (statusResp.IsSuccessStatusCode)
            {
                var statusBody = await statusResp.Content.ReadFromJsonAsync<RolloutStatusDto>(JsonOpts)!;
                status = statusBody!.Status;
            }
        }
        sw.Stop();

        status.Should().Be("Completed",
            $"rollout must complete within 15s (elapsed: {sw.Elapsed.TotalSeconds:F1}s)");
        sw.Elapsed.TotalSeconds.Should().BeLessThan(15);
    }

    [Fact]
    public async Task DriftEndpoint_1000Nodes_RespondWithin_3s()
    {
        var admin      = await AdminClientAsync();
        var templateId = await CreateAndPublishTemplateAsync(admin);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var toAdd = new List<SyncNode>();
            for (int i = 1; i <= 1000; i++)
            {
                var nid = $"drift-perf-{i:0000}";
                if (!await db.Nodes.AnyAsync(n => n.NodeId == nid))
                    toAdd.Add(new SyncNode
                    {
                        NodeId                        = nid,
                        GroupId                       = "cfg-group",
                        SyncUrl                       = $"http://{nid}.test",
                        LifecycleState                = NodeLifecycleState.Active,
                        AssignedTemplateId            = templateId,
                        AssignedTemplateVersion       = 1,
                        AppliedTemplateVersion        = 1,
                        ConfigurationState            = ConfigurationState.Drifted,
                        ConfigurationStatusReportedAt = DateTime.UtcNow.AddMinutes(-1),
                    });
            }
            db.Nodes.AddRange(toAdd);
            await db.SaveChangesAsync();
        }

        var sw   = Stopwatch.StartNew();
        var resp = await admin.GetAsync("/api/v1/configuration/drift?state=Drifted");
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.Elapsed.TotalSeconds.Should().BeLessThan(3.0,
            "drift endpoint must respond within 3s with 1000 drifted nodes");
    }

    [Fact]
    public async Task TemplateList_DoesNotLoadVersionContent()
    {
        var admin = await AdminClientAsync();

        for (int i = 0; i < 5; i++)
        {
            var r = await admin.PostAsJsonAsync("/api/v1/configuration/templates",
                new { name = $"lazy-tmpl-{Guid.NewGuid():N}", description = "Lazy test",
                      initialSettings = new { heartbeatIntervalSeconds = 30, batchSizeLimit = 100 } });
            r.EnsureSuccessStatusCode();
        }

        var sw       = Stopwatch.StartNew();
        var listResp = await admin.GetAsync("/api/v1/configuration/templates");
        sw.Stop();

        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await listResp.Content.ReadAsStringAsync();
        // List returns TemplateSummaryDto which has no settingsJson
        body.Should().NotContain("settingsJson",
            "template list must not include raw settings JSON (lazy load)");

        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(1000,
            "template list should be fast (under 1s)");
    }

    private sealed record TemplateApiDto(Guid Id, string Name, string Status);
    private sealed record RolloutAcceptedDto(Guid RolloutId, string Status);
    private sealed record RolloutStatusDto(string Status, int TargetNodeCount, int AppliedCount, int FailedCount);
}
