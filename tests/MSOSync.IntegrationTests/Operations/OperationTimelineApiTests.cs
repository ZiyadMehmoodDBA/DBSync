// tests/MSOSync.IntegrationTests/Operations/OperationTimelineApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Operations;

[Collection("Operations")]
public sealed class OperationTimelineApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/operations/timeline";

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Fmt(DateTime dt) => dt.ToString("o");

    private static async Task SeedOperationsAsync(
        AppDbContext db, int count, DateTime baseTime, string type = "Export")
    {
        for (var i = 0; i < count; i++)
        {
            db.Operations.Add(new SyncOperation
            {
                OperationId   = Guid.NewGuid(),
                OperationType = type,
                Status        = "Succeeded",
                Source        = "Test",
                CanCancel     = false,
                CanRetry      = false,
                StartedAt     = baseTime.AddMinutes(i),
                CompletedAt   = baseTime.AddMinutes(i).AddSeconds(30),
            });
        }
        await db.SaveChangesAsync();
    }

    // ── happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_ValidRange_Returns200WithItems()
    {
        using var scope = fixture.Services.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var from = DateTime.UtcNow.AddHours(-2);
        await SeedOperationsAsync(db, 3, from.AddMinutes(1));

        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync(
            $"{Base}?from={Fmt(from)}&to={Fmt(DateTime.UtcNow)}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        body.GetProperty("hasMore").ValueKind.Should().Be(JsonValueKind.False);
        body.GetProperty("returnedCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetTimeline_ViewerToken_Returns200()
    {
        var from   = DateTime.UtcNow.AddHours(-1);
        var client = await fixture.ViewerClientAsync();
        var resp   = await client.GetAsync(
            $"{Base}?from={Fmt(from)}&to={Fmt(DateTime.UtcNow)}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTimeline_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync(
            $"{Base}?from={Fmt(DateTime.UtcNow.AddHours(-1))}&to={Fmt(DateTime.UtcNow)}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── HasMore signaling ────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_LimitExceeded_HasMoreIsTrue()
    {
        using var scope = fixture.Services.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var from = DateTime.UtcNow.AddHours(-3);
        await SeedOperationsAsync(db, 6, from.AddMinutes(1), "Rollout");

        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync(
            $"{Base}?from={Fmt(from)}&to={Fmt(DateTime.UtcNow)}&limit=3");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        body.GetProperty("returnedCount").GetInt32().Should().Be(3);
    }

    // ── validation errors ────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_FromAfterTo_Returns400()
    {
        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync(
            $"{Base}?from={Fmt(DateTime.UtcNow)}&to={Fmt(DateTime.UtcNow.AddHours(-1))}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTimeline_RangeExceeds30Days_Returns400()
    {
        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync(
            $"{Base}?from={Fmt(DateTime.UtcNow.AddDays(-31))}&to={Fmt(DateTime.UtcNow)}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTimeline_TypeFilter_Returns200OnlyMatchingType()
    {
        using var scope = fixture.Services.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var from = DateTime.UtcNow.AddHours(-4);
        await SeedOperationsAsync(db, 2, from.AddMinutes(5), "BatchReplay");

        var client = await fixture.AdminClientAsync();
        var resp = await client.GetAsync(
            $"{Base}?from={Fmt(from)}&to={Fmt(DateTime.UtcNow)}&types=BatchReplay");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in body.GetProperty("items").EnumerateArray())
            item.GetProperty("type").GetString().Should().Be("BatchReplay");
    }
}
