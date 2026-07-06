using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.NodeManagement;

[Collection("NodeManagement")]
public sealed class ProvisionTests(NodeManagementFixture fixture)
{
    // ── GET /overview ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_Returns200_WithStats()
    {
        var client = await fixture.ViewerClientAsync();

        var resp = await client.GetAsync("api/v1/node-management/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("totalNodes",            out _).Should().BeTrue();
        body.TryGetProperty("pendingRegistrations",  out _).Should().BeTrue();
        body.TryGetProperty("pendingRecoveries",     out _).Should().BeTrue();
        body.TryGetProperty("generatedAt",           out _).Should().BeTrue();
        body.GetProperty("generatedAt").GetDateTime().Should()
            .BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetOverview_Unauthenticated_Returns401()
    {
        var client = fixture.AnonymousClient();

        var resp = await client.GetAsync("api/v1/node-management/overview");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /provision ───────────────────────────────────────────────────────

    [Fact]
    public async Task Provision_Returns201_WithNodeIdAndToken()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.PostAsJsonAsync("api/v1/node-management/provision", new
        {
            nodeName   = "test-provision-node",
            externalId = "prov-ext-001",
            nodeType   = "source",
            dbServer   = "sql-server-host",
            dbName     = "SyncDB",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("nodeId").GetString().Should().NotBeNullOrEmpty();
        var token = body.GetProperty("token").GetString();
        token.Should().NotBeNullOrEmpty();
        // Token is base64url — no '+', '/', '=' (URL-safe alphabet)
        token!.Should().NotContainAny("+", "/", "=");
    }

    [Fact]
    public async Task Provision_ViewerRole_Returns403()
    {
        var client = await fixture.ViewerClientAsync();

        var resp = await client.PostAsJsonAsync("api/v1/node-management/provision", new
        {
            nodeName   = "blocked-node",
            externalId = "blocked-ext-001",
            nodeType   = "source",
            dbServer   = "sql",
            dbName     = "db",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Provision_MissingRequiredField_Returns400()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.PostAsJsonAsync("api/v1/node-management/provision", new
        {
            // nodeName omitted
            externalId = "missing-name-ext",
            nodeType   = "source",
            dbServer   = "sql",
            dbName     = "db",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /provision-package ───────────────────────────────────────────────

    [Fact]
    public async Task ProvisionPackage_Returns200_ZipWithFiveFiles()
    {
        var client = await fixture.AdminClientAsync();

        // First provision a node to get a valid nodeId + token
        var provResp = await client.PostAsJsonAsync("api/v1/node-management/provision", new
        {
            nodeName   = "pkg-test-node",
            externalId = "pkg-ext-001",
            nodeType   = "target",
            dbServer   = "sql-pkg-host",
            dbName     = "PkgDB",
        });
        provResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var prov    = await provResp.Content.ReadFromJsonAsync<JsonElement>();
        var nodeId  = prov.GetProperty("nodeId").GetString()!;
        var token   = prov.GetProperty("token").GetString()!;

        // Download the package
        var pkgResp = await client.PostAsJsonAsync(
            "api/v1/node-management/provision-package",
            new { nodeId, token });

        pkgResp.StatusCode.Should().Be(HttpStatusCode.OK);
        pkgResp.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
        pkgResp.Content.Headers.ContentDisposition?.FileName.Should()
            .Contain(nodeId);

        // Verify ZIP structure — must contain exactly 5 files
        var zipBytes = await pkgResp.Content.ReadAsByteArrayAsync();
        using var stream  = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.Entries.Select(e => e.Name).Should().BeEquivalentTo(
            new[]
            {
                "msosync-node.json",
                ".env.example",
                "README.md",
                "manifest.json",
                "checksums.txt",
            });
    }

    [Fact]
    public async Task ProvisionPackage_MissingToken_Returns400()
    {
        var client = await fixture.AdminClientAsync();

        var resp = await client.PostAsJsonAsync(
            "api/v1/node-management/provision-package",
            new { nodeId = "some-node" }); // token omitted

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
