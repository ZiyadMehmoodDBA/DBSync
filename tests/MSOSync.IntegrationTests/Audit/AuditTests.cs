using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.Audit;

[Collection("Audit")]
public sealed class AuditTests(AuditFixture fixture)
{
    private async Task<HttpClient> ViewerClientAsync()
    {
        var token  = await fixture.GetViewerTokenAsync();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var token  = await fixture.GetAdminTokenAsync();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── GET /audit ─────────────────────────────────────────────

    [Fact]
    public async Task GetAudits_Returns200_WithSeededData()
    {
        var client = await ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/audit");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // totalCount includes login audit rows created by auth calls in other tests;
        // assert at least 3 seeded rows are present
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetAudits_FilterByUsername_ReturnsFiltered()
    {
        var client = await ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/audit?username=alice");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetAuditById_Found_Returns200()
    {
        var client = await ViewerClientAsync();

        // Resolve alice's actual audit id (identity-generated) via the list endpoint
        var listResp = await client.GetAsync("api/v1/audit?username=alice");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var aliceId = list.GetProperty("items")[0].GetProperty("auditId").GetInt64();

        var resp = await client.GetAsync($"api/v1/audit/{aliceId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("auditId").GetInt64().Should().Be(aliceId);
        body.GetProperty("username").GetString().Should().Be("alice");
    }

    [Fact]
    public async Task GetAuditById_NotFound_Returns404()
    {
        var client = await ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/audit/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /locks ─────────────────────────────────────────────

    [Fact]
    public async Task GetLocks_Returns200_WithSeededLocks()
    {
        var client = await ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/locks");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var locks = body.EnumerateArray().ToList();
        // At least RETRY_ENGINE is always present (SYNC_ENGINE may be deleted by DeleteLock test)
        locks.Should().NotBeEmpty();
        locks.Should().Contain(l => l.GetProperty("lockName").GetString() == "RETRY_ENGINE");
    }

    // ── DELETE /locks/{name} ──────────────────────────────────

    [Fact]
    public async Task DeleteLock_AdminOnly_Returns204()
    {
        var client = await AdminClientAsync();

        var resp = await client.DeleteAsync("api/v1/locks/SYNC_ENGINE");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone
        var getResp = await client.GetAsync("api/v1/locks");
        var body    = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateArray().ToList()
            .Should().NotContain(l => l.GetProperty("lockName").GetString() == "SYNC_ENGINE");
    }
}
