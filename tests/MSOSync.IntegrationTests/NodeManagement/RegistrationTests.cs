using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.NodeManagement;

[Collection("NodeManagement")]
public sealed class RegistrationTests(NodeManagementFixture fixture)
{
    // ── GET /registrations ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrations_Returns200_WithSeededItems()
    {
        var client = await fixture.ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/node-management/registrations?includeTotalCount=true");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetRegistrations_FilterByPending_ReturnsOnlyPending()
    {
        var client = await fixture.ViewerClientAsync();

        var resp = await client.GetAsync(
            "api/v1/node-management/registrations?status=Pending&includeTotalCount=true");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().AllSatisfy(i =>
            i.GetProperty("status").GetString().Should().Be("Pending"));
    }

    [Fact]
    public async Task GetRegistrations_Unauthenticated_Returns401()
    {
        var client = fixture.AnonymousClient();

        var resp = await client.GetAsync("api/v1/node-management/registrations");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /registrations/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrationDetail_Found_Returns200()
    {
        var client = await fixture.ViewerClientAsync();

        // Get the first pending registration id from the list
        var listResp = await client.GetAsync(
            "api/v1/node-management/registrations?status=Pending");
        var list  = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var id    = list.GetProperty("items")[0].GetProperty("id").GetInt64();

        var resp = await client.GetAsync($"api/v1/node-management/registrations/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetInt64().Should().Be(id);
        body.TryGetProperty("status", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetRegistrationDetail_NotFound_Returns404()
    {
        var client = await fixture.ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/node-management/registrations/99999999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /registrations (inbound, anonymous) ──────────────────────────────

    [Fact]
    public async Task InboundRegistration_Returns202_WithRegistrationId()
    {
        var client = fixture.AnonymousClient();

        var resp = await client.PostAsJsonAsync("api/v1/node-management/registrations", new
        {
            externalId = "test-node-ext-999",
            nodeName   = "test-node",
            nodeType   = "source",
            metadata   = new { schemaVersion = 1, machine = new { hostName = "test-host" } },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("registrationId").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task InboundRegistration_InvalidNodeType_Returns400()
    {
        var client = fixture.AnonymousClient();

        var resp = await client.PostAsJsonAsync("api/v1/node-management/registrations", new
        {
            externalId = "test-node-ext-bad",
            nodeName   = "test-node",
            nodeType   = "invalid-type",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /registrations/{id}/approve ──────────────────────────────────────

    [Fact]
    public async Task ApproveRegistration_Returns204_UpdatesStatus()
    {
        var client        = await fixture.ApproverClientAsync();
        var viewerClient  = await fixture.ViewerClientAsync();

        // Register a new node so we have a fresh pending registration independent of other tests
        var anon = fixture.AnonymousClient();
        var regResp = await anon.PostAsJsonAsync("api/v1/node-management/registrations", new
        {
            externalId = "node-to-approve-ext",
            nodeName   = "node-to-approve",
            nodeType   = "source",
        });
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var id      = regBody.GetProperty("registrationId").GetInt64();

        var resp = await client.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id}/approve",
            new { notes = "looks good" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify status changed
        var detailResp = await viewerClient.GetAsync(
            $"api/v1/node-management/registrations/{id}");
        var detail = await detailResp.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("status").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task ApproveRegistration_ViewerRole_Returns403()
    {
        var viewerClient = await fixture.ViewerClientAsync();
        var anon         = fixture.AnonymousClient();

        // Register a node so there is something to reference (id doesn't matter for 403 path)
        var regResp = await anon.PostAsJsonAsync("api/v1/node-management/registrations", new
        {
            externalId = "node-for-403-test",
            nodeName   = "node-for-403",
            nodeType   = "source",
        });
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var id      = regBody.GetProperty("registrationId").GetInt64();

        var resp = await viewerClient.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id}/approve",
            new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /registrations/{id}/reject ───────────────────────────────────────

    [Fact]
    public async Task RejectRegistration_Returns204_UpdatesStatus()
    {
        var client       = await fixture.ApproverClientAsync();
        var viewerClient = await fixture.ViewerClientAsync();

        // Register a fresh node so we have a pending one independent of approve test
        var anon = fixture.AnonymousClient();
        var regResp = await anon.PostAsJsonAsync("api/v1/node-management/registrations", new
        {
            externalId = "node-to-reject-ext",
            nodeName   = "node-to-reject",
            nodeType   = "target",
        });
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var id      = regBody.GetProperty("registrationId").GetInt64();

        var resp = await client.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id}/reject",
            new { reason = "not authorized network" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await viewerClient.GetAsync(
            $"api/v1/node-management/registrations/{id}");
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Rejected");
    }

    // ── Bulk ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkApprove_Returns207_WithMixedStatuses()
    {
        var approver  = await fixture.ApproverClientAsync();
        var anon      = fixture.AnonymousClient();

        // Register 2 new nodes
        var id1 = await PostRegistrationAsync(anon, "bulk-node-1");
        var id2 = await PostRegistrationAsync(anon, "bulk-node-2");

        // Pre-approve id2 so it will be "AlreadyApproved"
        await approver.PostAsJsonAsync(
            $"api/v1/node-management/registrations/{id2}/approve", new { });

        var resp = await approver.PostAsJsonAsync(
            "api/v1/node-management/registrations/bulk-approve",
            new { ids = new[] { id1, id2, 99999999L } });

        resp.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var statuses = items.EnumerateArray()
            .Select(i => i.GetProperty("status").GetString())
            .ToList();
        statuses.Should().Contain("Approved");
        statuses.Should().Contain("AlreadyApproved");
        statuses.Should().Contain("NotFound");
    }

    [Fact]
    public async Task BulkReject_Returns207_AllRejected()
    {
        var approver = await fixture.ApproverClientAsync();
        var anon     = fixture.AnonymousClient();

        var id1 = await PostRegistrationAsync(anon, "bulk-reject-node-1");
        var id2 = await PostRegistrationAsync(anon, "bulk-reject-node-2");

        var resp = await approver.PostAsJsonAsync(
            "api/v1/node-management/registrations/bulk-reject",
            new { ids = new[] { id1, id2 }, reason = "batch rejected" });

        resp.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement>();
        items.EnumerateArray()
            .Select(i => i.GetProperty("status").GetString())
            .Should().AllBe("Rejected");
    }

    // ── Re-registration diff ─────────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrationDetail_ReRegistration_HasDiff()
    {
        var viewer = await fixture.ViewerClientAsync();

        // The seeded re-registration for "node-ext-001" has metadata so diff is computed
        var listResp = await viewer.GetAsync(
            "api/v1/node-management/registrations?registrationType=ReRegistration");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var id   = list.GetProperty("items")[0].GetProperty("id").GetInt64();

        var resp = await viewer.GetAsync(
            $"api/v1/node-management/registrations/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // diff is not null for re-registrations with metadata
        body.TryGetProperty("diff", out var diff).Should().BeTrue();
        diff.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<long> PostRegistrationAsync(HttpClient client, string nodeId)
    {
        var resp = await client.PostAsJsonAsync("api/v1/node-management/registrations", new
        {
            externalId = nodeId,
            nodeName   = nodeId,
            nodeType   = "source",
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("registrationId").GetInt64();
    }
}
