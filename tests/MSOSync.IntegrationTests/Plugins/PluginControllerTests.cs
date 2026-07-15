// tests/MSOSync.IntegrationTests/Plugins/PluginControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Plugins;

[Collection("Plugins")]
public sealed class PluginControllerTests(PluginsFixture fx)
{
    // ── GET /api/v1/plugins ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPlugins_Unauthenticated_Returns401()
    {
        var resp = await fx.AnonClient().GetAsync("/api/v1/plugins");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPlugins_AsAdmin_Returns200WithTestPlugin()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var plugins = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        plugins.Should().NotBeNull();
        // msosync.test should be present and loaded (entry type exists in the DLL)
        var testPlugin = plugins!.FirstOrDefault(p =>
            p.GetProperty("pluginId").GetString() == "msosync.test");
        testPlugin.Should().NotBeNull("test plugin must be discovered");
    }

    // ── GET /api/v1/plugins/summary ─────────────────────────────────────────

    [Fact]
    public async Task GetPluginSummary_ReturnsCorrectCounts()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/summary");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("loaded").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // ── GET /api/v1/plugins/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task GetPlugin_KnownId_Returns200()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/msosync.test");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pluginId").GetString().Should().Be("msosync.test");
    }

    [Fact]
    public async Task GetPlugin_UnknownId_Returns404()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/no.such.plugin");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/v1/plugins/{id}/manifest ────────────────────────────────────

    [Fact]
    public async Task GetPluginManifest_Returns200WithFields()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/msosync.test/manifest");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be("msosync.test");
        body.GetProperty("entryType").GetString().Should().Be("MSOSync.TestPlugin.TestPlugin");
    }

    // ── POST /api/v1/plugins/{id}/enable|disable ─────────────────────────────

    [Fact]
    public async Task DisablePlugin_ReturnsRestartRequired()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.PostAsync("/api/v1/plugins/msosync.test/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("restartRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task EnablePlugin_ReturnsRestartRequired()
    {
        var client = await fx.AdminClientAsync();
        // First disable, then re-enable
        await client.PostAsync("/api/v1/plugins/msosync.test/disable", null);
        var resp = await client.PostAsync("/api/v1/plugins/msosync.test/enable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("restartRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DisablePlugin_UnknownId_Returns404()
    {
        var client = await fx.AdminClientAsync();
        var resp   = await client.PostAsync("/api/v1/plugins/no.such.plugin/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Registry state ────────────────────────────────────────────────────────

    [Fact]
    public async Task PluginHost_ValidPlugin_RegistersAsLoaded()
    {
        // The registry is populated at startup. The test plugin should be Loaded.
        var client = await fx.AdminClientAsync();
        var resp   = await client.GetAsync("/api/v1/plugins/msosync.test");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Loaded");
    }

    [Fact]
    public async Task PluginHost_HealthCheck_ReturnsHealthyWhenLoaded()
    {
        var client = fx.CreateClient();
        var resp   = await client.GetAsync("/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
