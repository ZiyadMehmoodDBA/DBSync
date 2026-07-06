# Epic 12A Task 2: Registration APIs

> **For agentic workers:** This is Task 2 of 7 for Epic 12A. Task 1 must be complete before starting this task (it defines all DTOs, services, and database entities).

**Goal:** Build the `NodeManagementController` registration endpoints (7 endpoints), FluentValidation validators, and integration tests for the registration lifecycle.

**Builds on Task 1:** All DTOs (`InboundRegistrationDto`, `RegistrationSummaryDto`, `RegistrationDetailDto`, `ApproveRegistrationRequest`, `RejectRegistrationRequest`, `BulkApproveRequest`, `BulkRejectRequest`, `BulkResultItemDto`, `RegistrationListFilter`), services (`INodeManagementService`, `INodeLifecycleService`), and enums (`RegistrationType`, `RegistrationStatus`) are already defined. Do not redefine them.

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`
- FluentValidation 11.11.0 — validators auto-discovered in `MSOSync.Api` via `AddValidatorsFromAssemblyContaining<AuthController>()`; filter validators registered explicitly in `AddMetadata()`
- `INodeLifecycleService` is the ONLY orchestration point — never call diff service or provision service from controller directly
- Bulk endpoints return 207 Multi-Status, not 200/400
- `POST /registrations` is `[AllowAnonymous]` (agent-facing, no UI auth)
- Integration tests use LocalDB (`(localdb)\\mssqllocaldb`), following the established WAF fixture pattern
- xUnit 2.9.3, FluentAssertions 6.12.2

## Files

**Create:**
- `src/MSOSync.Api/Controllers/NodeManagementController.cs` — 7 registration endpoints (overview + provision added in Task 3)
- `src/MSOSync.Api/Validators/InboundRegistrationDtoValidator.cs`
- `src/MSOSync.Api/Validators/ApproveRegistrationRequestValidator.cs`
- `src/MSOSync.Api/Validators/RejectRegistrationRequestValidator.cs`
- `src/MSOSync.Api/Validators/BulkApproveRequestValidator.cs`
- `src/MSOSync.Api/Validators/BulkRejectRequestValidator.cs`
- `src/MSOSync.Metadata/NodeManagement/RegistrationListFilterValidator.cs`
- `tests/MSOSync.IntegrationTests/NodeManagement/NodeManagementFixture.cs`
- `tests/MSOSync.IntegrationTests/NodeManagement/RegistrationTests.cs`

**Modify:**
- `src/MSOSync.Metadata/MetadataServiceExtensions.cs` — register `RegistrationListFilterValidator`

## Interfaces Consumed (from Task 1)

```csharp
// INodeManagementService — read side
Task<CursorPageResult<RegistrationSummaryDto>> GetRegistrationsAsync(RegistrationListFilter filter, CancellationToken ct);
Task<RegistrationDetailDto?> GetRegistrationDetailAsync(long id, CancellationToken ct);

// INodeLifecycleService — write side
Task<long>   RegisterAsync(InboundRegistrationDto dto, CancellationToken ct);
Task         ApproveAsync(long id, string? notes, string actorUsername, CancellationToken ct);
Task         RejectAsync(long id, string? reason, string actorUsername, CancellationToken ct);
Task<IReadOnlyList<BulkResultItemDto>> BulkApproveAsync(IReadOnlyList<long> ids, string actorUsername, CancellationToken ct);
Task<IReadOnlyList<BulkResultItemDto>> BulkRejectAsync(IReadOnlyList<long> ids, string? reason, string actorUsername, CancellationToken ct);

// DTOs (from NodeManagementDtos.cs)
public sealed class RegistrationListFilter
{
    public RegistrationStatus? Status { get; set; }
    public RegistrationType? RegistrationType { get; set; }
    public int PageSize { get; set; } = 50;
    public string? Cursor { get; set; }
    public bool IncludeTotalCount { get; set; }
}

public sealed record InboundRegistrationDto(string ExternalId, string NodeName, string NodeType, RegistrationMetadataDto? Metadata);
public sealed record ApproveRegistrationRequest(string? Notes);
public sealed record RejectRegistrationRequest(string? Reason);
public sealed record BulkApproveRequest(IReadOnlyList<long> Ids);
public sealed record BulkRejectRequest(IReadOnlyList<long> Ids, string? Reason);
public sealed record BulkResultItemDto(long Id, string Status);

// SystemPermissions (from Task 1)
SystemPermissions.ViewTopology  = "VIEW_TOPOLOGY"
SystemPermissions.ApproveNodes  = "APPROVE_NODES"
SystemPermissions.ManageUsers   = "MANAGE_USERS"
```

## Interfaces Produced (used by Tasks 3, 4+)

`NodeManagementController` is partial — Task 3 adds 3 more actions to this same class. Do NOT mark it partial in the C# sense; just know Task 3 will add methods to it.

---

## Steps

- [ ] **Step 1: Create RegistrationListFilterValidator**

```csharp
// src/MSOSync.Metadata/NodeManagement/RegistrationListFilterValidator.cs
using FluentValidation;

namespace MSOSync.Metadata.NodeManagement;

public sealed class RegistrationListFilterValidator : AbstractValidator<RegistrationListFilter>
{
    public RegistrationListFilterValidator()
    {
        RuleFor(f => f.PageSize).InclusiveBetween(1, 500);
    }
}
```

- [ ] **Step 2: Register RegistrationListFilterValidator in AddMetadata()**

In `src/MSOSync.Metadata/MetadataServiceExtensions.cs`, add at the bottom of `AddMetadata()` before `return services;`:

```csharp
// Epic 12A — Node Management
services.AddScoped<IValidator<NodeManagement.RegistrationListFilter>, NodeManagement.RegistrationListFilterValidator>();
```

Add `using MSOSync.Metadata.NodeManagement;` is NOT needed — use the fully-qualified name as shown, or add a using at the top of the file.

- [ ] **Step 3: Create API-level validators**

```csharp
// src/MSOSync.Api/Validators/InboundRegistrationDtoValidator.cs
using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class InboundRegistrationDtoValidator : AbstractValidator<InboundRegistrationDto>
{
    public InboundRegistrationDtoValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NodeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NodeType)
            .NotEmpty()
            .Must(t => t == "source" || t == "target")
            .WithMessage("NodeType must be 'source' or 'target'");
        RuleFor(x => x.Metadata!.SchemaVersion)
            .GreaterThanOrEqualTo(1)
            .WithMessage("metadata.schemaVersion must be >= 1")
            .When(x => x.Metadata is not null);
    }
}
```

```csharp
// src/MSOSync.Api/Validators/ApproveRegistrationRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class ApproveRegistrationRequestValidator : AbstractValidator<ApproveRegistrationRequest>
{
    public ApproveRegistrationRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(1000).When(x => x.Notes is not null);
    }
}
```

```csharp
// src/MSOSync.Api/Validators/RejectRegistrationRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class RejectRegistrationRequestValidator : AbstractValidator<RejectRegistrationRequest>
{
    public RejectRegistrationRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}
```

```csharp
// src/MSOSync.Api/Validators/BulkApproveRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class BulkApproveRequestValidator : AbstractValidator<BulkApproveRequest>
{
    public BulkApproveRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("ids must contain at least one entry");
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(100).WithMessage("ids must not exceed 100 items");
    }
}
```

```csharp
// src/MSOSync.Api/Validators/BulkRejectRequestValidator.cs
using FluentValidation;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Api.Validators;

public sealed class BulkRejectRequestValidator : AbstractValidator<BulkRejectRequest>
{
    public BulkRejectRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("ids must contain at least one entry");
        RuleFor(x => x.Ids.Count).LessThanOrEqualTo(100).WithMessage("ids must not exceed 100 items");
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}
```

- [ ] **Step 4: Create NodeManagementController with all 7 registration endpoints**

```csharp
// src/MSOSync.Api/Controllers/NodeManagementController.cs
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Permissions;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/node-management")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class NodeManagementController(
    INodeManagementService              nodeManagement,
    INodeLifecycleService               lifecycle,
    IPermissionService                  permissionService,
    ICurrentUserService                 currentUser,
    IValidator<RegistrationListFilter>  listValidator)
    : ControllerBase
{
    // ── Registration read ──────────────────────────────────────────────────────

    [HttpGet("registrations")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> GetRegistrations(
        [FromQuery] RegistrationListFilter filter, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ViewTopology))
            return Forbid();

        await listValidator.ValidateAndThrowAsync(filter, ct);
        return Ok(await nodeManagement.GetRegistrationsAsync(filter, ct));
    }

    [HttpGet("registrations/{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetRegistrationDetail(long id, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ViewTopology))
            return Forbid();

        var dto = await nodeManagement.GetRegistrationDetailAsync(id, ct);
        if (dto is null) throw new NotFoundException($"Registration {id} not found.");
        return Ok(dto);
    }

    // ── Inbound registration (agent-facing, no UI auth) ───────────────────────

    [HttpPost("registrations")]
    [AllowAnonymous]
    [ProducesResponseType(202)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> InboundRegistration(
        [FromBody] InboundRegistrationDto dto, CancellationToken ct)
    {
        var id = await lifecycle.RegisterAsync(dto, ct);
        return StatusCode(202, new { registrationId = id });
    }

    // ── Approve / Reject ───────────────────────────────────────────────────────

    [HttpPost("registrations/{id:long}/approve")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> ApproveRegistration(
        long id, [FromBody] ApproveRegistrationRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        await lifecycle.ApproveAsync(id, request.Notes, currentUser.GetCurrentUsername(), ct);
        return NoContent();
    }

    [HttpPost("registrations/{id:long}/reject")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> RejectRegistration(
        long id, [FromBody] RejectRegistrationRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        await lifecycle.RejectAsync(id, request.Reason, currentUser.GetCurrentUsername(), ct);
        return NoContent();
    }

    // ── Bulk ───────────────────────────────────────────────────────────────────

    [HttpPost("registrations/bulk-approve")]
    [ProducesResponseType(207)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> BulkApprove(
        [FromBody] BulkApproveRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        var results = await lifecycle.BulkApproveAsync(
            request.Ids, currentUser.GetCurrentUsername(), ct);
        return StatusCode(207, results);
    }

    [HttpPost("registrations/bulk-reject")]
    [ProducesResponseType(207)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<IActionResult> BulkReject(
        [FromBody] BulkRejectRequest request, CancellationToken ct)
    {
        var perms = await permissionService.GetEffectivePermissionsAsync(
            currentUser.GetCurrentUsername(), ct);
        if (!perms.Permissions.Contains(SystemPermissions.ApproveNodes))
            return Forbid();

        var results = await lifecycle.BulkRejectAsync(
            request.Ids, request.Reason, currentUser.GetCurrentUsername(), ct);
        return StatusCode(207, results);
    }
}
```

- [ ] **Step 5: Create NodeManagementFixture**

```csharp
// tests/MSOSync.IntegrationTests/NodeManagement/NodeManagementFixture.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Controllers.Auth;
using MSOSync.Api.Exceptions;
using MSOSync.App;
using MSOSync.Common;
using MSOSync.Metadata;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using MSOSync.Topology;
using Xunit;

namespace MSOSync.IntegrationTests.NodeManagement;

public sealed class NodeManagementFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncNodeMgmt_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private const string JwtSecret = "test-jwt-secret-value-at-least-32-chars!";

    public string ViewerUsername    { get; } = "nm-viewer";
    public string ViewerPassword    { get; } = "ViewP@ss1!";
    public string ApproverUsername  { get; } = "nm-approver";
    public string ApproverPassword  { get; } = "ApprP@ss1!";
    public string AdminUsername     { get; } = "nm-admin";
    public string AdminPassword     { get; } = "AdminP@ss1!";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testBuilder = WebApplication.CreateBuilder();
        testBuilder.WebHost.UseTestServer();

        testBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnStr,
            ["Jwt:Secret"]                          = JwtSecret,
            ["Jwt:Issuer"]                          = "msosync",
            ["Jwt:Audience"]                        = "msosync-dashboard",
            ["Jwt:AccessExpiryMinutes"]             = "60",
            ["RateLimit:LoginPermitLimit"]          = "100",
            ["RateLimit:RefreshPermitLimit"]        = "100",
        });

        testBuilder.Services.AddPersistence(testBuilder.Configuration);
        testBuilder.Services.AddSecurity(testBuilder.Configuration);
        testBuilder.Services.AddMetadata(testBuilder.Configuration);
        testBuilder.Services.AddSingleton<IClock, SystemClock>();
        testBuilder.Services.AddTopologyServices();
        testBuilder.Services.AddHttpContextAccessor();
        testBuilder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
        testBuilder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        testBuilder.Services.AddProblemDetails();

        testBuilder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly);

        testBuilder.Services.AddFluentValidationAutoValidation();
        testBuilder.Services.AddValidatorsFromAssemblyContaining<AuthController>();

        var app = testBuilder.Build();

        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseSecurityHeaders();
        app.UseAuthentication();
        app.UseNodeTokenAuth();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "UP" }));

        app.Start();
        return app;
    }

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);

        if (await db.Database.CanConnectAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncNodeMgmt_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await db.Database.EnsureDeletedAsync();
        }

        await db.Database.MigrateAsync();

        foreach (var role in new[] { "ADMIN", "OPERATOR", "VIEWER" })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == role))
                db.Roles.Add(new SyncRole { RoleName = role });
        }
        await db.SaveChangesAsync();

        // Grant permissions per role
        await GrantIfMissingAsync(db, "VIEWER",   "VIEW_TOPOLOGY");
        await GrantIfMissingAsync(db, "OPERATOR", "VIEW_TOPOLOGY");
        await GrantIfMissingAsync(db, "OPERATOR", "APPROVE_NODES");
        await GrantIfMissingAsync(db, "ADMIN",    "VIEW_TOPOLOGY");
        await GrantIfMissingAsync(db, "ADMIN",    "APPROVE_NODES");
        await GrantIfMissingAsync(db, "ADMIN",    "MANAGE_USERS");
        await db.SaveChangesAsync();

        var hasher = new BCryptPasswordHasher();
        await CreateUserAsync(db, hasher, ViewerUsername,   ViewerPassword,   "VIEWER");
        await CreateUserAsync(db, hasher, ApproverUsername, ApproverPassword, "OPERATOR");
        await CreateUserAsync(db, hasher, AdminUsername,    AdminPassword,    "ADMIN");

        await SeedAsync(db);
    }

    private static async Task GrantIfMissingAsync(AppDbContext db, string roleName, string permissionKey)
    {
        var exists = await db.RolePermissions.AnyAsync(
            rp => rp.RoleName == roleName && rp.PermissionKey == permissionKey);
        if (!exists)
            db.RolePermissions.Add(new SyncRolePermission
            {
                RoleName      = roleName,
                PermissionKey = permissionKey,
            });
    }

    private static async Task CreateUserAsync(
        AppDbContext db, BCryptPasswordHasher hasher,
        string username, string password, string roleName)
    {
        if (await db.Users.AnyAsync(u => u.Username == username)) return;
        var user = new SyncUser
        {
            Username     = username,
            PasswordHash = hasher.Hash(password),
            Enabled      = true,
            CreatedTime  = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var role = await db.Roles.FirstAsync(r => r.RoleName == roleName);
        db.UserRoles.Add(new SyncUserRole { UserId = user.UserId, RoleId = role.RoleId });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        if (await db.RegistrationRequests.AnyAsync()) return;

        // Seed a SyncNode for re-registration tests
        var node = new SyncNode
        {
            ExternalId        = "node-ext-001",
            NodeName          = "seeded-node",
            NodeGroup         = "group-a",
            NodeStatus        = MSOSync.Persistence.Entities.NodeStatus.Active,
            SyncUrl           = "http://node1:8080",
            HeartbeatInterval = 60,
            IsHub             = false,
        };
        db.Nodes.Add(node);
        await db.SaveChangesAsync();

        db.RegistrationRequests.AddRange(
            new MSOSync.Persistence.Entities.SyncRegistrationRequest
            {
                NodeId            = "node-ext-001",
                NodeName          = "seeded-node",
                RegistrationType  = MSOSync.Persistence.Entities.RegistrationType.ReRegistration,
                Status            = MSOSync.Persistence.Entities.RegistrationStatus.Pending,
                RequestTime       = DateTime.UtcNow.AddMinutes(-30),
            },
            new MSOSync.Persistence.Entities.SyncRegistrationRequest
            {
                NodeId            = "node-ext-002",
                NodeName          = "new-node",
                RegistrationType  = MSOSync.Persistence.Entities.RegistrationType.New,
                Status            = MSOSync.Persistence.Entities.RegistrationStatus.Pending,
                RequestTime       = DateTime.UtcNow.AddMinutes(-20),
            },
            new MSOSync.Persistence.Entities.SyncRegistrationRequest
            {
                NodeId            = "node-ext-003",
                NodeName          = "approved-node",
                RegistrationType  = MSOSync.Persistence.Entities.RegistrationType.New,
                Status            = MSOSync.Persistence.Entities.RegistrationStatus.Approved,
                RequestTime       = DateTime.UtcNow.AddMinutes(-60),
                ProcessedAt       = DateTime.UtcNow.AddMinutes(-50),
                ProcessedBy       = "admin",
            });
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnStr).Options;
        await using var db = new AppDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSyncNodeMgmt_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    public async Task<HttpClient> ViewerClientAsync()   => await MakeClientAsync(ViewerUsername,   ViewerPassword);
    public async Task<HttpClient> ApproverClientAsync() => await MakeClientAsync(ApproverUsername, ApproverPassword);
    public async Task<HttpClient> AdminClientAsync()    => await MakeClientAsync(AdminUsername,    AdminPassword);
    public HttpClient AnonymousClient()                 => CreateClient();

    private async Task<HttpClient> MakeClientAsync(string username, string password)
    {
        var loginClient = CreateClient();
        var resp = await loginClient.PostAsJsonAsync("api/v1/auth/login",
            new { username, password });
        resp.EnsureSuccessStatusCode();
        var body  = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

[CollectionDefinition("NodeManagement")]
public sealed class NodeManagementCollection : ICollectionFixture<NodeManagementFixture> { }
```

- [ ] **Step 6: Write failing integration tests**

```csharp
// tests/MSOSync.IntegrationTests/NodeManagement/RegistrationTests.cs
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

        // Get a pending registration
        var listResp = await viewerClient.GetAsync(
            "api/v1/node-management/registrations?status=Pending");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var id   = list.GetProperty("items")[0].GetProperty("id").GetInt64();

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

        var listResp = await viewerClient.GetAsync(
            "api/v1/node-management/registrations?status=Pending");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var id   = list.GetProperty("items")[0].GetProperty("id").GetInt64();

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

        // The seeded re-registration for "node-ext-001"
        var listResp = await viewer.GetAsync(
            "api/v1/node-management/registrations?registrationType=ReRegistration");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var id   = list.GetProperty("items")[0].GetProperty("id").GetInt64();

        var resp = await viewer.GetAsync(
            $"api/v1/node-management/registrations/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // diff is not null for re-registrations
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
```

- [ ] **Step 7: Run failing tests**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement.RegistrationTests" -c Debug
```

Expected: FAIL — `NodeManagementController` not yet in DI / routing (or build errors if controller is not yet registered).

- [ ] **Step 8: Build and run tests, fix any compilation errors**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~NodeManagement.RegistrationTests" -c Debug
```

The controller is in `MSOSync.Api` which is already loaded via `AddApplicationPart(typeof(AuthController).Assembly)` in the fixture. Tests should pass after the controller is created and validators are in place.

Expected: All tests GREEN.

- [ ] **Step 9: Commit**

```pwsh
git add `
  src/MSOSync.Api/Controllers/NodeManagementController.cs `
  src/MSOSync.Api/Validators/InboundRegistrationDtoValidator.cs `
  src/MSOSync.Api/Validators/ApproveRegistrationRequestValidator.cs `
  src/MSOSync.Api/Validators/RejectRegistrationRequestValidator.cs `
  src/MSOSync.Api/Validators/BulkApproveRequestValidator.cs `
  src/MSOSync.Api/Validators/BulkRejectRequestValidator.cs `
  src/MSOSync.Metadata/NodeManagement/RegistrationListFilterValidator.cs `
  src/MSOSync.Metadata/MetadataServiceExtensions.cs `
  tests/MSOSync.IntegrationTests/NodeManagement/NodeManagementFixture.cs `
  tests/MSOSync.IntegrationTests/NodeManagement/RegistrationTests.cs
git commit -m "feat(12A): registration APIs + integration tests"
```
