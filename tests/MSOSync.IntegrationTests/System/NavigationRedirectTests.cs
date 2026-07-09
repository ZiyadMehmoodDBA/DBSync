// tests/MSOSync.IntegrationTests/System/NavigationRedirectTests.cs
using System.Net;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

/// <summary>
/// Navigation redirect tests verify that backend HTTP endpoints enforce
/// authentication correctly. Frontend route redirects (React Navigate component)
/// are covered by the TypeScript build and manual smoke tests in Task 12.
/// </summary>
[Collection("SystemAdmin")]
public sealed class NavigationRedirectTests(SystemFixture fx)
{
    // ── Unauthenticated access to all 12C endpoints must return 401 ────────────

    [Fact]
    public async Task SystemInfo_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/info");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemOverview_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemWorkers_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/workers");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemHealth_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/system/health");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CorrelationTimeline_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/audit/correlation/some-id");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CorrelationSearch_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/audit/correlations/search");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Operations_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/operations");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parameters_Unauthenticated_Returns401()
    {
        var anon = fx.CreateClient();
        var resp = await anon.GetAsync("/api/v1/parameters");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authenticated access to read endpoints must succeed ────────────────────

    [Fact]
    public async Task SystemInfo_Authenticated_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/info");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SystemOverview_Authenticated_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/overview");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SystemWorkers_Authenticated_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/workers");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SystemHealth_Authenticated_Returns200()
    {
        var admin = await fx.AdminClientAsync();
        var resp  = await admin.GetAsync("/api/v1/system/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
