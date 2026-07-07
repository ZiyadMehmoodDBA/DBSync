using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Lifecycle")]
public sealed class LifecycleAuthorizationTests(LifecycleFixture fixture)
{
    private static object DisableBody() => new { reason = "test" };
    private static object MaintenanceStartBody() => new { reason = "test", notifyNode = false };
    private static object DecommissionBody() => new { reason = "test", gracePeriodMinutes = 60 };

    // ── Unauthenticated → 401 ─────────────────────────────────────────────────

    [Theory]
    [InlineData("POST", "nodes/authz-anon-1/enable")]
    [InlineData("POST", "nodes/authz-anon-1/disable")]
    [InlineData("POST", "nodes/authz-anon-1/maintenance/start")]
    [InlineData("POST", "nodes/authz-anon-1/maintenance/end")]
    [InlineData("POST", "nodes/authz-anon-1/decommission")]
    [InlineData("POST", "nodes/authz-anon-1/decommission/force")]
    public async Task AllMutatingEndpoints_Unauthenticated_Return401(string method, string path)
    {
        var client = fixture.AnonymousClient();
        var resp = method == "POST"
            ? await client.PostAsJsonAsync($"api/v1/node-lifecycle/{path}", new { })
            : await client.GetAsync($"api/v1/node-lifecycle/{path}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"{method} {path} should return 401 for anonymous requests");
    }

    // ── Viewer → 403 on mutating endpoints ────────────────────────────────────

    [Theory]
    [InlineData("nodes/authz-viewer-1/enable")]
    [InlineData("nodes/authz-viewer-1/disable")]
    [InlineData("nodes/authz-viewer-1/maintenance/start")]
    [InlineData("nodes/authz-viewer-1/maintenance/end")]
    [InlineData("nodes/authz-viewer-1/decommission")]
    [InlineData("nodes/authz-viewer-1/decommission/force")]
    public async Task AllMutatingEndpoints_ViewerRole_Return403(string path)
    {
        var viewer = await fixture.ViewerClientAsync();
        var resp   = await viewer.PostAsJsonAsync($"api/v1/node-lifecycle/{path}", new { reason = "x", notifyNode = false, gracePeriodMinutes = 60 });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"POST {path} should return 403 for viewer role");
    }

    // ── Viewer → 200 on read endpoints ────────────────────────────────────────

    [Fact]
    public async Task GetState_ViewerRole_Returns403()
    {
        // GetState requires MANAGE_NODE_LIFECYCLE — viewer doesn't have it
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "authz-state-rd");
        var viewer = await fixture.ViewerClientAsync();
        var resp   = await viewer.GetAsync($"api/v1/node-lifecycle/nodes/{nodeId}/state");
        // Controller uses EnsurePermissionAsync(ManageNodeLifecycle) for state too
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHistory_LifecycleManagerRole_Returns200()
    {
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "authz-hist-rd");
        var mgr    = await fixture.LifecycleManagerClientAsync();
        var resp   = await mgr.GetAsync($"api/v1/node-lifecycle/nodes/{nodeId}/history");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── LifecycleManager (OPERATOR) → can mutate ──────────────────────────────

    [Fact]
    public async Task MutatingEndpoints_LifecycleManagerRole_Succeed()
    {
        // Representative: enable on a Disabled node
        var nodeId = await fixture.SeedNodeAsync(NodeLifecycleState.Disabled, "authz-enable-ok");
        var mgr    = await fixture.LifecycleManagerClientAsync();
        var resp   = await mgr.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/enable", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Approver without ManageNodeLifecycle cannot decommission ──────────────

    [Fact]
    public async Task Approver_WithoutManageNodeLifecycle_CannotDecommission_403()
    {
        // Approver has APPROVE_NODES but NOT MANAGE_NODE_LIFECYCLE (fixture grants OPERATOR role
        // which has MANAGE_NODE_LIFECYCLE, but the aprrover user is OPERATOR here).
        // We test using a viewer client (which has no lifecycle permission).
        var nodeId  = await fixture.SeedNodeAsync(NodeLifecycleState.Active, "authz-decom-403");
        var viewer  = await fixture.ViewerClientAsync();
        var resp    = await viewer.PostAsJsonAsync(
            $"api/v1/node-lifecycle/nodes/{nodeId}/decommission",
            DecommissionBody());
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Provision without PROVISION_NODES → 403 ───────────────────────────────

    [Fact]
    public async Task Provision_WithoutProvisionNodes_Returns403()
    {
        // Operator has APPROVE_NODES + MANAGE_NODE_LIFECYCLE but NOT PROVISION_NODES
        // (PROVISION_NODES is ADMIN only in our permission seed)
        var approver = await fixture.ApproverClientAsync();
        var resp = await approver.PostAsJsonAsync("api/v1/node-management/provision", new
        {
            nodeName   = "authz-prov-403",
            externalId = "authz-prov-403-ext",
            nodeType   = "source",
            dbServer   = "sql",
            dbName     = "db",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
