// tests/MSOSync.IntegrationTests/System/OperationRegistryTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[Collection("SystemAdmin")]
public sealed class OperationRegistryTests(SystemFixture fx)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── GET /api/v1/operations ─────────────────────────────────────────────────

    [Fact]
    public async Task ListOperations_Admin_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/operations");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.TryGetProperty("items",      out _).Should().BeTrue("response must include an items array");
        body.TryGetProperty("totalCount", out _).Should().BeTrue("response must include a totalCount");
    }

    [Fact]
    public async Task ListOperations_WithTypeFilter_ReturnsCorrectSubset()
    {
        // Seed 2 Export + 2 Rollout operations
        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Operations.AddRange(
                new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export",  Status = "Completed", Source = "System", StartedAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid().ToString() },
                new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export",  Status = "Completed", Source = "System", StartedAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid().ToString() },
                new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Rollout", Status = "Pending",   Source = "System", StartedAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid().ToString() },
                new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Rollout", Status = "Pending",   Source = "System", StartedAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid().ToString() }
            );
            await db.SaveChangesAsync();
        }

        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/operations?types=Export");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty("there are seeded Export operations");
        items.Should().AllSatisfy(op =>
            op.GetProperty("operationType").GetString()
              .Should().Be("Export", "type filter must exclude non-Export operations"));
    }

    [Fact]
    public async Task GetOperation_NotFound_Returns404()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync($"/api/v1/operations/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListOperations_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/operations");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/v1/operations/{id}/cancel ────────────────────────────────────

    [Fact]
    public async Task CancelOperation_RunningAndCancellable_SetsCancelled()
    {
        var opId = Guid.NewGuid();

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Operations.Add(new SyncOperation
            {
                OperationId   = opId,
                OperationType = "Export",
                Status        = "Running",
                Source        = "User",
                StartedAt     = DateTime.UtcNow,
                CanCancel     = true,
                CorrelationId = Guid.NewGuid().ToString(),
            });
            await db.SaveChangesAsync();
        }

        var admin = await fx.AdminClientAsync();
        var resp  = await admin.PostAsJsonAsync($"/api/v1/operations/{opId}/cancel", new { });

        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.NoContent },
            "cancel of a running cancellable operation must succeed");

        await using var scope2 = fx.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var op  = await db2.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.OperationId == opId);
        op.Should().NotBeNull();
        op!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task CancelOperation_Viewer_Returns403()
    {
        var opId = Guid.NewGuid();

        await using (var scope = fx.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Operations.Add(new SyncOperation
            {
                OperationId   = opId,
                OperationType = "Rollout",
                Status        = "Running",
                Source        = "User",
                StartedAt     = DateTime.UtcNow,
                CanCancel     = true,
                CorrelationId = Guid.NewGuid().ToString(),
            });
            await db.SaveChangesAsync();
        }

        var viewer = await fx.ViewerClientAsync();
        var resp   = await viewer.PostAsJsonAsync($"/api/v1/operations/{opId}/cancel", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "VIEWER must not be able to cancel operations");
    }
}
