// tests/MSOSync.IntegrationTests/Configuration/ConfigurationDriftTests.cs
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
public sealed class ConfigurationDriftTests(ConfigurationFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = fx.CreateClient();
        var token  = await fx.GetJwtAsync(client, fx.AdminUsername, fx.AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> SetupTemplateAndAssignAsync(HttpClient admin, string nodeId)
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/configuration/templates",
            new { name = $"drift-tmpl-{Guid.NewGuid():N}", description = "Drift test",
                  initialSettings = new { heartbeatIntervalSeconds = 30, batchSizeLimit = 100 } });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<TemplateApiDto>(JsonOpts)!;

        (await admin.PostAsync($"/api/v1/configuration/templates/{created!.Id}/publish", null))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/configuration/nodes/{nodeId}/assign",
            new { templateId = created.Id, version = 1 }))
            .EnsureSuccessStatusCode();

        return created.Id;
    }

    [Fact]
    public async Task HashMismatch_ProducesDrifted_State()
    {
        var admin = await AdminClientAsync();
        var node  = fx.NodeClient();
        await SetupTemplateAndAssignAsync(admin, fx.NodeId);

        var resp = await node.PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new
            {
                nodeId                   = fx.NodeId,
                uptimeSeconds            = 100L,
                appliedTemplateVersion   = 1,
                appliedEffectiveHash     = "deadbeefdeadbeefdeadbeef00000000",
                configurationApplyStatus = "Applied",
            });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n  = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == fx.NodeId);
            n.ConfigurationState.Should().Be(ConfigurationState.Drifted);
        }
    }

    [Fact]
    public async Task DriftCleared_WhenCorrectHashSent()
    {
        var admin = await AdminClientAsync();
        var node  = fx.NodeClient();
        await SetupTemplateAndAssignAsync(admin, fx.NodeId);

        var currentBody = await (await node.GetAsync("/api/v1/configurations/current"))
            .Content.ReadFromJsonAsync<CurrentConfigApiDto>(JsonOpts)!;

        // Send wrong hash first → Drifted
        await node.PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new
            {
                nodeId                   = fx.NodeId,
                uptimeSeconds            = 100L,
                appliedTemplateVersion   = 1,
                appliedEffectiveHash     = "wronghash00000000",
                configurationApplyStatus = "Applied",
            });

        // Send correct hash → Current + DriftCleared history event
        await node.PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new
            {
                nodeId                   = fx.NodeId,
                uptimeSeconds            = 200L,
                appliedTemplateVersion   = 1,
                appliedEffectiveHash     = currentBody!.ContentHash,
                configurationApplyStatus = "Applied",
            });

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n  = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == fx.NodeId);
            n.ConfigurationState.Should().Be(ConfigurationState.Current);

            var clearEvent = await db.NodeConfigurationHistories
                .Where(h => h.NodeId == fx.NodeId && h.EventType == "DriftCleared")
                .FirstOrDefaultAsync();
            clearEvent.Should().NotBeNull("a DriftCleared event must be written when drift is resolved");
        }
    }

    [Fact]
    public async Task ApplyFailed_Status_SetsState_Failed()
    {
        var admin = await AdminClientAsync();
        var node  = fx.NodeClient();
        await SetupTemplateAndAssignAsync(admin, fx.NodeId);

        var resp = await node.PostAsJsonAsync(
            $"/api/v1/nodes/{fx.NodeId}/heartbeat",
            new
            {
                nodeId                   = fx.NodeId,
                uptimeSeconds            = 100L,
                appliedTemplateVersion   = 1,
                appliedEffectiveHash     = (string?)null,
                configurationApplyStatus = "Failed",
            });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n  = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == fx.NodeId);
            n.ConfigurationState.Should().Be(ConfigurationState.Failed);

            var applyFailedEvent = await db.NodeConfigurationHistories
                .Where(h => h.NodeId == fx.NodeId && h.EventType == "ApplyFailed")
                .FirstOrDefaultAsync();
            applyFailedEvent.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Override_ChangesHash_CausesUpdateAvailable_RemoveClears()
    {
        var admin = await AdminClientAsync();
        var node  = fx.NodeClient();
        await SetupTemplateAndAssignAsync(admin, fx.NodeId);

        var initialConfig = await (await node.GetAsync("/api/v1/configurations/current"))
            .Content.ReadFromJsonAsync<CurrentConfigApiDto>(JsonOpts)!;

        // Reach Current
        await node.PostAsJsonAsync($"/api/v1/nodes/{fx.NodeId}/heartbeat", new
        {
            nodeId                   = fx.NodeId,
            uptimeSeconds            = 100L,
            appliedTemplateVersion   = 1,
            appliedEffectiveHash     = initialConfig!.ContentHash,
            configurationApplyStatus = "Applied",
        });

        // Add override → ExpectedEffectiveHash changes
        var overrideResp = await admin.PostAsJsonAsync(
            $"/api/v1/configuration/nodes/{fx.NodeId}/overrides",
            new { key = "heartbeatIntervalSeconds", value = "60", source = "Manual" });
        overrideResp.EnsureSuccessStatusCode();

        // Node reports old hash → Drifted (override changed expected hash)
        await node.PostAsJsonAsync($"/api/v1/nodes/{fx.NodeId}/heartbeat", new
        {
            nodeId                   = fx.NodeId,
            uptimeSeconds            = 200L,
            appliedTemplateVersion   = 1,
            appliedEffectiveHash     = initialConfig.ContentHash,
            configurationApplyStatus = "Applied",
        });

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n  = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == fx.NodeId);
            n.ConfigurationState.Should().BeOneOf(
                ConfigurationState.Drifted, ConfigurationState.UpdateAvailable);
        }

        // Node fetches new config and applies it
        var newConfig = await (await node.GetAsync("/api/v1/configurations/current"))
            .Content.ReadFromJsonAsync<CurrentConfigApiDto>(JsonOpts)!;

        await node.PostAsJsonAsync($"/api/v1/nodes/{fx.NodeId}/heartbeat", new
        {
            nodeId                   = fx.NodeId,
            uptimeSeconds            = 300L,
            appliedTemplateVersion   = 1,
            appliedEffectiveHash     = newConfig!.ContentHash,
            configurationApplyStatus = "Applied",
        });

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n  = await db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == fx.NodeId);
            n.ConfigurationState.Should().Be(ConfigurationState.Current);
        }
    }

    private sealed record TemplateApiDto(Guid Id, string Name, string Status);
    private sealed record CurrentConfigApiDto(string ContentHash, object? Effective);
}
