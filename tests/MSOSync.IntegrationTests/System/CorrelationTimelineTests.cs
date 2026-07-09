// tests/MSOSync.IntegrationTests/System/CorrelationTimelineTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[Collection("SystemAdmin")]
public sealed class CorrelationTimelineTests(SystemFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── GET /api/v1/audit/correlation/{correlationId} ──────────────────────────

    [Fact]
    public async Task GetCorrelation_WithAuditEvents_ReturnsTimeline()
    {
        var correlationId = Guid.NewGuid().ToString();

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (int i = 0; i < 3; i++)
            {
                db.Audits.Add(new SyncAudit
                {
                    CorrelationId = correlationId,
                    ActionName    = $"NODE_ACTIVATED",
                    ObjectName    = $"Test event {i} for correlation test",
                    Username      = "sys-admin",
                    CreateTime    = DateTime.UtcNow.AddSeconds(-i * 10),
                });
            }
            await db.SaveChangesAsync();
        }

        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync(
            $"/api/v1/audit/correlation/{Uri.EscapeDataString(correlationId)}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("correlationId").GetString().Should().Be(correlationId);
        body.GetProperty("totalEventCount").GetInt32().Should().Be(3);
        body.GetProperty("phases").GetArrayLength().Should().BeGreaterThan(0,
            "events must be grouped into at least one phase");
    }

    [Fact]
    public async Task GetCorrelation_NotFound_Returns404()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync(
            "/api/v1/audit/correlation/nonexistent-correlation-id-that-does-not-exist-xyz789");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCorrelation_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/audit/correlation/some-id");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCorrelation_IsFailedWorkflow_TrueWhenErrorActionPresent()
    {
        var correlationId = Guid.NewGuid().ToString();

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Audits.Add(new SyncAudit
            {
                CorrelationId = correlationId,
                ActionName    = "ROLLOUT_FAILED",
                ObjectName    = "Rollout failed due to network error",
                Username      = "sys-admin",
                CreateTime    = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync(
            $"/api/v1/audit/correlation/{Uri.EscapeDataString(correlationId)}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("isFailedWorkflow").GetBoolean().Should().BeTrue(
            "a FAILED action name must set isFailedWorkflow=true");
    }

    // ── GET /api/v1/audit/correlations/search ──────────────────────────────────

    [Fact]
    public async Task SearchCorrelations_ByCorrelationIdPrefix_ReturnsMatch()
    {
        var correlationId = "searchtest-" + Guid.NewGuid().ToString("N")[..8];

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Audits.Add(new SyncAudit
            {
                CorrelationId = correlationId,
                ActionName    = "NODE_APPROVED",
                ObjectName    = "Search test event",
                Username      = "sys-admin",
                CreateTime    = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync(
            $"/api/v1/audit/correlations/search?q={Uri.EscapeDataString(correlationId)}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await resp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        results.Should().NotBeNull();
        results!.Should().Contain(r =>
            r.GetProperty("correlationId").GetString() == correlationId,
            "searching for the exact correlationId must return it");
    }

    [Fact]
    public async Task SearchCorrelations_EmptyQuery_ReturnsResults()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/audit/correlations/search");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await resp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        results.Should().NotBeNull("empty query must return an array (possibly empty)");
    }

    [Fact]
    public async Task SearchCorrelations_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/audit/correlations/search");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
