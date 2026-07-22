// tests/MSOSync.IntegrationTests/Audit/AuditExplorerTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Audit;

[Collection("Audit")]
public sealed class AuditExplorerTests(AuditFixture fixture)
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> ViewerClientAsync()
    {
        var token  = await fixture.GetViewerTokenAsync();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── multi-value Usernames[] filter ────────────────────────────────────

    [Fact]
    public async Task GetAudit_MultipleUsernames_ReturnsUnionOfBothUsers()
    {
        var client = await ViewerClientAsync();

        // alice has 2 rows, bob has 1 row — total 3
        // ASP.NET Core binds repeated query param keys to arrays (without [] suffix)
        var resp = await client.GetAsync(
            "api/v1/audit?Usernames=alice&Usernames=bob&includeTotalCount=true");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetAudit_MultipleActionNames_ReturnsMatchingRows()
    {
        var client = await ViewerClientAsync();

        // UPDATE and DELETE rows exist in seed data
        var resp = await client.GetAsync(
            "api/v1/audit?ActionNames=UPDATE&ActionNames=DELETE&includeTotalCount=true");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetAudit_MultipleObjectNames_ReturnsMatchingRows()
    {
        var client = await ViewerClientAsync();

        // SyncNode and SyncTrigger are in seed data
        var resp = await client.GetAsync(
            "api/v1/audit?ObjectNames=SyncNode&ObjectNames=SyncTrigger&includeTotalCount=true");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetAudit_TooManyUsernames_Returns400()
    {
        var client  = await ViewerClientAsync();
        // 11 usernames — exceeds max of 10 → validator throws → 400
        var tooMany = string.Join("&", Enumerable.Range(1, 11).Select(i => $"Usernames=user{i}"));

        var resp = await client.GetAsync($"api/v1/audit?{tooMany}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── entity history endpoint ───────────────────────────────────────────

    [Fact]
    public async Task GetEntityHistory_KnownObjectName_Returns200WithMatchingRows()
    {
        var client = await ViewerClientAsync();

        // "SyncNode" appears in seed: alice UPDATE SyncNode
        var resp = await client.GetAsync("api/v1/audit/entity/SyncNode");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        // All returned rows should match objectName
        foreach (var item in body.GetProperty("items").EnumerateArray())
            item.GetProperty("objectName").GetString().Should().Be("SyncNode");
    }

    [Fact]
    public async Task GetEntityHistory_UnknownObjectName_Returns200WithEmptyItems()
    {
        var client = await ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/audit/entity/NonExistentEntity");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetEntityHistory_NoToken_Returns401()
    {
        var client = fixture.CreateClient();
        var resp   = await client.GetAsync("api/v1/audit/entity/SyncNode");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
