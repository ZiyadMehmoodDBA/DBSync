// tests/MSOSync.IntegrationTests/System/OverviewTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[Collection("SystemAdmin")]
public sealed class OverviewTests(SystemFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── GET /api/v1/system/overview ────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_Admin_Returns200WithAllWidgets()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        // Verify all expected top-level widgets are present
        body.TryGetProperty("health",       out _).Should().BeTrue("overview must include a health widget");
        body.TryGetProperty("operations",   out _).Should().BeTrue("overview must include an operations widget");
        body.TryGetProperty("nodes",        out _).Should().BeTrue("overview must include a nodes widget");
        body.TryGetProperty("lastRefreshedAt", out _).Should().BeTrue("overview must include a timestamp");
    }

    [Fact]
    public async Task GetOverview_HealthWidget_HasExpectedFields()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body   = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var health = body.GetProperty("health");
        health.TryGetProperty("clusterHealth", out _).Should().BeTrue();
        health.TryGetProperty("workerHealth",  out _).Should().BeTrue();
        health.TryGetProperty("nodeHealth",    out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetOverview_Viewer_Returns200()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.GetAsync("/api/v1/system/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "VIEWER role should have read-only access to the overview");
    }

    [Fact]
    public async Task GetOverview_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/v1/system/info ────────────────────────────────────────────────

    [Fact]
    public async Task GetSystemInfo_ReturnsVersionAndEdition()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.GetAsync("/api/v1/system/info");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("edition").GetString().Should().Be("Community");
        body.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("environment").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSystemInfo_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/info");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/v1/system/health ──────────────────────────────────────────────

    [Fact]
    public async Task GetSystemHealth_ReturnsContributors()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/health");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var contributors = await resp.Content.ReadFromJsonAsync<HealthContributorResponse[]>(JsonOpts);
        contributors.Should().NotBeNullOrEmpty("at least one health contributor must be registered");
        contributors!.Should().AllSatisfy(c =>
        {
            c.Name.Should().NotBeNullOrEmpty("contributor must have a name");
            c.Level.Should().BeOneOf("Healthy", "Degraded", "Unhealthy",
                $"contributor '{c.Name}' has an unrecognized level");
        });
    }

    [Fact]
    public async Task GetSystemHealth_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/health");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Response record types ──────────────────────────────────────────────────
    private record HealthContributorResponse(string Name, string Level, string Summary, string? Detail);
}
