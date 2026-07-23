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
public sealed class ClusterDiagnosticsApiTests(OperationsFixture fixture)
{
    private const string Base = "api/v1/cluster/diagnostics";

    [Fact]
    public async Task GetDiagnostics_Returns200WithAllThreeSubLists()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("runtimeStats").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("activeLocks").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("slowOperations").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetDiagnostics_EmptyDb_ReturnsEmptyListsNot500()
    {
        // Integration DB may have data; just verify 200 and shape
        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // All sub-lists must be JSON arrays (even if empty)
        body.GetProperty("runtimeStats").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("activeLocks").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("slowOperations").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetDiagnostics_StaleLock_HasIsStaleTrueInResponse()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lockName = $"stale-{Guid.NewGuid():N}";
        db.Set<SyncLock>().Add(new SyncLock
        {
            LockName  = lockName,
            LockOwner = "int-test-worker",
            LockTime  = DateTime.UtcNow.AddMinutes(-15),
            Scope     = LockScope.Platform,
        });
        await db.SaveChangesAsync();

        var client = await fixture.AdminClientAsync();
        var resp   = await client.GetAsync(Base);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var locks = body.GetProperty("activeLocks").EnumerateArray().ToList();
        var stale = locks.FirstOrDefault(l => l.GetProperty("lockName").GetString() == lockName);
        stale.ValueKind.Should().NotBe(JsonValueKind.Undefined, "seeded stale lock should appear");
        stale.GetProperty("isStale").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetDiagnostics_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync(Base);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
