// tests/MSOSync.IntegrationTests/System/AdministrationTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[Collection("SystemAdmin")]
public sealed class AdministrationTests(SystemFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── GET /api/v1/parameters ─────────────────────────────────────────────────

    [Fact]
    public async Task GetParameters_NoFilter_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/parameters");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items.Should().NotBeNull("endpoint must return an array");
    }

    [Fact]
    public async Task GetParameters_WithFeatureFlagCategory_ReturnsOnlyFlagCategory()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/parameters?category=FeatureFlag");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items.Should().NotBeNull();

        // The seeded SYS_TEST_FLAG has Category=FeatureFlag
        items!.Should().NotBeEmpty("a FeatureFlag parameter was seeded in InitializeAsync");
        items.Should().AllSatisfy(p =>
        {
            p.TryGetProperty("category", out var cat).Should().BeTrue("each parameter must have a category");
            cat.GetString().Should().Be("FeatureFlag",
                "category filter must only return FeatureFlag parameters");
        });
    }

    [Fact]
    public async Task GetParameters_WithRetentionCategory_ReturnsOnlyRetentionParams()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/parameters?category=Retention");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items.Should().NotBeNull();

        // SYS_RETENTION_DAYS is seeded in InitializeAsync — the array must never be empty
        items!.Should().NotBeEmpty("a Retention parameter (SYS_RETENTION_DAYS) was seeded in InitializeAsync");
        items.Should().AllSatisfy(p =>
        {
            p.TryGetProperty("category", out var cat).Should().BeTrue("each parameter must have a category");
            cat.GetString().Should().Be("Retention",
                "category filter must only return Retention parameters");
        });
    }

    [Fact]
    public async Task GetParameters_Viewer_Returns200()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.GetAsync("/api/v1/parameters");

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "VIEWER role must have read access to parameters");
    }

    [Fact]
    public async Task GetParameters_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/parameters");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/v1/parameters/{name} ─────────────────────────────────────────

    [Fact]
    public async Task GetParameter_SeededFlag_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/parameters/SYS_TEST_FLAG");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("parameterName").GetString().Should().Be("SYS_TEST_FLAG");
    }

    [Fact]
    public async Task GetParameter_NotFound_Returns404()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/parameters/NONEXISTENT_PARAMETER_XYZ");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /api/v1/parameters/{name} ─────────────────────────────────────────

    [Fact]
    public async Task UpdateParameter_Viewer_Returns403()
    {
        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.PutAsJsonAsync(
            "/api/v1/parameters/SYS_TEST_FLAG",
            new { value = "false" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "VIEWER role must not be able to modify parameters");
    }

    [Fact]
    public async Task UpdateParameter_Admin_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.PutAsJsonAsync(
            "/api/v1/parameters/SYS_TEST_FLAG",
            new { value = "false" });

        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.NoContent },
            "ADMIN must be able to update a parameter");
    }

    [Fact]
    public async Task UpdateParameter_Admin_GeneratesAuditEvent()
    {
        var admin = await fx.AdminClientAsync();

        // Update the parameter
        var updateResp = await admin.PutAsJsonAsync(
            "/api/v1/parameters/SYS_TEST_FLAG",
            new { value = "true" });
        updateResp.EnsureSuccessStatusCode();

        // Verify audit event was generated
        await using var scope = fx.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var auditRow = await db.Audits
            .AsNoTracking()
            .Where(a => a.ActionName == "PARAMETER_UPDATED" &&
                        a.ObjectName != null &&
                        a.ObjectName.Contains("SYS_TEST_FLAG"))
            .FirstOrDefaultAsync();

        auditRow.Should().NotBeNull(
            "updating a parameter must generate a PARAMETER_UPDATED audit event with the parameter name in ObjectName");
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
    }
}
