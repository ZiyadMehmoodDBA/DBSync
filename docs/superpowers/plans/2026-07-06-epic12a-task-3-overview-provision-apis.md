# Epic 12A Task 3: Overview + Provision APIs

> **For agentic workers:** This is Task 3 of 7. Tasks 1 and 2 must be complete before starting. Task 2 created `NodeManagementController` with 7 registration endpoints; this task adds 3 more endpoints to that same controller.

**Goal:** Add the overview endpoint, provision endpoint, and streaming provision-package endpoint to `NodeManagementController`. Add provision integration tests.

**Builds on Tasks 1–2:** `INodeLifecycleService.ProvisionAsync`, `INodeManagementService.GetOverviewAsync`, `ProvisionPackageService`, and all related DTOs (`ProvisionRequestDto`, `ProvisionResultDto`, `NodeManagementOverviewDto`, `ProvisionPackageRequest`) are already defined. Do not redefine them.

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`
- ZIP streamed directly to `HttpResponse.Body` via `ZipArchive` — no intermediate `MemoryStream`; omit `Content-Length` header
- ZIP MIME type: `application/zip`
- Content-Disposition: `attachment; filename="msosync-node-{nodeId}.zip"`
- Token: 32-byte cryptographically random, base64url encoded, returned once in 201 body only — never logged
- `POST /provision` requires `MANAGE_USERS` permission
- `POST /provision-package` requires `MANAGE_USERS` permission
- xUnit 2.9.3, FluentAssertions 6.12.2

## Files

**Modify:**
- `src/MSOSync.Api/Controllers/NodeManagementController.cs` — add 3 endpoints

**Create:**
- `src/MSOSync.Api/Validators/ProvisionRequestValidator.cs`
- `src/MSOSync.Api/Validators/ProvisionPackageRequestValidator.cs`
- `tests/MSOSync.IntegrationTests/NodeManagement/ProvisionTests.cs`

## Interfaces Consumed (from Task 1)

```csharp
// INodeManagementService
Task<NodeManagementOverviewDto> GetOverviewAsync(CancellationToken ct);

// INodeLifecycleService
Task<ProvisionResultDto> ProvisionAsync(ProvisionRequestDto dto, string actorUsername, CancellationToken ct);

// IProvisionPackageService
Task StreamPackageAsync(string nodeId, string token, Stream destination, CancellationToken ct);

// DTOs
public sealed record ProvisionRequestDto(
    string  NodeName,
    string  ExternalId,
    string  NodeType,       // "source" | "target"
    string  DbServer,
    string  DbName,
    string? GroupId,
    string? Description);

public sealed record ProvisionResultDto(string NodeId, string Token);

public sealed record NodeManagementOverviewDto(
    int       PendingRegistrations,
    int       PendingRecoveries,
    int       TotalNodes,
    int       ActiveNodes,
    int       OfflineNodes,
    int       DegradedNodes,
    int       TotalGroups,
    DateTime? LastRegistrationAt,
    DateTime? LastApprovalAt,
    DateTime  GeneratedAt);

public sealed record ProvisionPackageRequest(string NodeId, string Token);
```

---

## Steps

- [ ] **Step 1: Add validators**

```csharp
// src/MSOSync.Api/Validators/ProvisionRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class ProvisionRequestValidator : AbstractValidator<ProvisionRequestDto>
{
    public ProvisionRequestValidator()
    {
        RuleFor(x => x.NodeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NodeType)
            .NotEmpty()
            .Must(t => t == "source" || t == "target")
            .WithMessage("NodeType must be 'source' or 'target'");
        RuleFor(x => x.DbServer).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DbName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupId).MaximumLength(100).When(x => x.GroupId is not null);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}
```

```csharp
// src/MSOSync.Api/Validators/ProvisionPackageRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class ProvisionPackageRequestValidator : AbstractValidator<ProvisionPackageRequest>
{
    public ProvisionPackageRequestValidator()
    {
        RuleFor(x => x.NodeId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty();
    }
}
```

- [ ] **Step 2: Add 3 endpoints to NodeManagementController**

Open `src/MSOSync.Api/Controllers/NodeManagementController.cs`. The constructor already injects `INodeManagementService nodeManagement`, `INodeLifecycleService lifecycle`, `IPermissionService permissionService`, `ICurrentUserService currentUser`. Add `IProvisionPackageService provisionPackage` to the constructor parameter list.

Add the following methods to the class (after the bulk-reject method):

```csharp
    // ── Overview ───────────────────────────────────────────────────────────────

    [HttpGet("overview")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ViewTopology))
            return Forbid();

        return Ok(await nodeManagement.GetOverviewAsync(ct));
    }

    // ── Provision ─────────────────────────────────────────────────────────────

    [HttpPost("provision")]
    [ProducesResponseType(201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> Provision(
        [FromBody] ProvisionRequestDto dto, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ManageUsers))
            return Forbid();

        var result = await lifecycle.ProvisionAsync(dto, currentUser.GetCurrentUsername(), ct);
        return StatusCode(201, new { nodeId = result.NodeId, token = result.Token });
    }

    [HttpPost("provision-package")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> GetProvisionPackage(
        [FromBody] ProvisionPackageRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ManageUsers))
            return Forbid();

        Response.ContentType = "application/zip";
        Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"msosync-node-{request.NodeId}.zip\"";
        await provisionPackage.StreamPackageAsync(
            request.NodeId, request.Token, Response.Body, ct);
        return new Microsoft.AspNetCore.Mvc.EmptyResult();
    }
```

Update the constructor signature to add `IProvisionPackageService provisionPackage`:

```csharp
public sealed class NodeManagementController(
    INodeManagementService              nodeManagement,
    INodeLifecycleService               lifecycle,
    IProvisionPackageService            provisionPackage,
    IPermissionService                  permissionService,
    ICurrentUserService                 currentUser,
    IValidator<RegistrationListFilter>  listValidator)
    : ControllerBase
```

- [ ] **Step 3: Write failing provision integration tests**

```csharp
// tests/MSOSync.IntegrationTests/NodeManagement/ProvisionTests.cs
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
```

- [ ] **Step 4: Run failing tests**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement.ProvisionTests" -c Debug
```

Expected: FAIL (controller endpoints not yet added).

- [ ] **Step 5: Build and run to green**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement" -c Debug
```

Expected: ALL NodeManagement integration tests GREEN (RegistrationTests + ProvisionTests).

- [ ] **Step 6: Commit**

```pwsh
git add `
  src/MSOSync.Api/Controllers/NodeManagementController.cs `
  src/MSOSync.Api/Validators/ProvisionRequestValidator.cs `
  src/MSOSync.Api/Validators/ProvisionPackageRequestValidator.cs `
  tests/MSOSync.IntegrationTests/NodeManagement/ProvisionTests.cs
git commit -m "feat(12A): overview + provision endpoints + provision integration tests"
```
