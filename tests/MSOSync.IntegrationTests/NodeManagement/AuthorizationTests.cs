using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.NodeManagement;

[Collection("NodeManagement")]
public sealed class AuthorizationTests(NodeManagementFixture fixture)
{
    // ── Unauthenticated → 401 ────────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrations_Unauthenticated_Returns401()
    {
        var resp = await fixture.AnonymousClient()
            .GetAsync("api/v1/node-management/registrations");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOverview_Unauthenticated_Returns401()
    {
        var resp = await fixture.AnonymousClient()
            .GetAsync("api/v1/node-management/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Provision_Unauthenticated_Returns401()
    {
        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/node-management/provision", new
            {
                nodeName   = "x",
                externalId = "x",
                nodeType   = "source",
                dbServer   = "s",
                dbName     = "d",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── VIEWER cannot approve/reject/provision → 403 ─────────────────────────

    [Fact]
    public async Task Approve_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/1/approve", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reject_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/1/reject", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BulkApprove_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/bulk-approve",
            new { ids = new[] { 1L } });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BulkReject_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/registrations/bulk-reject",
            new { ids = new[] { 1L } });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Provision_ViewerRole_Returns403()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            "api/v1/node-management/provision", new
            {
                nodeName   = "blocked",
                externalId = "blocked-ext",
                nodeType   = "source",
                dbServer   = "sql",
                dbName     = "db",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── APPROVER (OPERATOR) cannot provision → 403 ────────────────────────────

    [Fact]
    public async Task Provision_ApproverRole_Returns403()
    {
        var approver = await fixture.ApproverClientAsync();

        var resp = await approver.PostAsJsonAsync(
            "api/v1/node-management/provision", new
            {
                nodeName   = "blocked-approver",
                externalId = "blocked-approver-ext",
                nodeType   = "source",
                dbServer   = "sql",
                dbName     = "db",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── APPROVER can read → 200 ───────────────────────────────────────────────

    [Fact]
    public async Task GetRegistrations_ApproverRole_Returns200()
    {
        var approver = await fixture.ApproverClientAsync();

        var resp = await approver.GetAsync("api/v1/node-management/registrations");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── VIEWER can read → 200 ─────────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_ViewerRole_Returns200()
    {
        var viewer = await fixture.ViewerClientAsync();

        var resp = await viewer.GetAsync("api/v1/node-management/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /registrations is anonymous → 202 ────────────────────────────────

    [Fact]
    public async Task InboundRegistration_Anonymous_Returns202()
    {
        var resp = await fixture.AnonymousClient()
            .PostAsJsonAsync("api/v1/node-management/registrations", new
            {
                externalId = "anon-auth-test-node",
                nodeName   = "anon-node",
                nodeType   = "source",
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
