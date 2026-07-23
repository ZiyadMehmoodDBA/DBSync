# Task 2: Cursor Pagination on Node Endpoints

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `CursorToken.EncodeString`/`DecodeString` to `MSOSync.Common`, delegate from `CursorSigner`, add `GET /api/v1/nodes/cursor`, gate `GET /api/v1/nodes` behind a configurable count threshold, and add a `Deprecation` header to `GET /api/v1/nodes/paged`.

**Prerequisite:** T1 (M038 migration) should be applied so the `IX_sync_node_group_id` index covering `(group_id, node_id)` exists. The code works without it but will be slower.

## Files

- Modify: `src/MSOSync.Common/Pagination/CursorToken.cs`
- Modify: `src/MSOSync.Metadata/Pagination/CursorSigner.cs`
- Create: `src/MSOSync.Metadata/Options/PaginationOptions.cs`
- Create: `src/MSOSync.Metadata/NodeManagement/NodeCursorFilter.cs`
- Create: `src/MSOSync.Api/Dtos/Nodes/NodeListGateResponse.cs`
- Modify: `src/MSOSync.Metadata/Interfaces/INodeMetadataService.cs`
- Modify: `src/MSOSync.Metadata/Services/NodeMetadataService.cs`
- Modify: `src/MSOSync.Api/Controllers/NodesController.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Modify: `src/MSOSync.App/appsettings.json`
- Create: `tests/MSOSync.MetadataTests/Scale/NodeCursorPaginationTests.cs`

## Interfaces

**Produces (consumed by T5 benchmarks):**

```csharp
// In INodeMetadataService:
Task<CursorPageResult<NodeDto>> GetNodesCursorAsync(NodeCursorFilter filter, CancellationToken ct = default);
Task<NodeListGateResult>        GetNodesWithGateAsync(int threshold, CancellationToken ct = default);

// New CursorToken static methods:
public static string EncodeString(string id, long ticks, ReadOnlySpan<byte> hmacKey);
public static (string Id, long Ticks) DecodeString(string token, ReadOnlySpan<byte> hmacKey);

// New CursorSigner instance methods:
public string EncodeString(string id, long ticks);
public (string Id, long Ticks) DecodeString(string token);
```

## Steps

- [ ] **Step 1: Write failing tests for `CursorToken.EncodeString`/`DecodeString`**

Create `tests/MSOSync.MetadataTests/Scale/NodeCursorPaginationTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Options;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Services;
using MSOSync.Metadata.Dtos;
using MSOSync.Persistence.Entities;
using Microsoft.Extensions.Caching.Memory;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Moq;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests.Scale;

public sealed class CursorTokenStringTests
{
    private static readonly byte[] Key = new byte[32]; // all-zeros dev key

    [Fact]
    public void EncodeString_ThenDecodeString_RoundTrips()
    {
        const string nodeId = "node-abc-123";
        long ticks = DateTime.UtcNow.Ticks;

        var token = CursorToken.EncodeString(nodeId, ticks, Key);
        var (id, decodedTicks) = CursorToken.DecodeString(token, Key);

        id.Should().Be(nodeId);
        decodedTicks.Should().Be(ticks);
    }

    [Fact]
    public void DecodeString_TamperedToken_Throws()
    {
        const string nodeId = "node-abc-123";
        var token = CursorToken.EncodeString(nodeId, 0L, Key);

        // Corrupt last char
        var corrupt = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var act = () => CursorToken.DecodeString(corrupt, Key);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecodeString_GarbageInput_Throws()
    {
        var act = () => CursorToken.DecodeString("not-base64!!!", Key);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EncodeString_ProducesOpaqueToken_NotContainingNodeId()
    {
        const string nodeId = "node-secret-id";
        var token = CursorToken.EncodeString(nodeId, 0L, Key);

        // The raw token must NOT contain the node ID in plain text
        token.Should().NotContain(nodeId);
    }

    [Fact]
    public void CursorSigner_EncodeString_DelegatesToCursorToken()
    {
        var signer = new CursorSigner(new byte[32]);
        const string nodeId = "node-xyz";
        long ticks = 12345L;

        var token = signer.EncodeString(nodeId, ticks);
        var (id, t) = signer.DecodeString(token);

        id.Should().Be(nodeId);
        t.Should().Be(ticks);
    }
}

public sealed class NodeCursorPaginationTests
{
    private static (NodeMetadataService Svc, MSOSync.Persistence.AppDbContext Db) Make()
    {
        var db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = new Mock<IMediator>().Object;
        var hasher = new BCryptPasswordHasher();
        var nodeSecurity = new NodeSecurityService(db, hasher);
        var protectorMock = new Mock<IDataProtector>();
        protectorMock.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        protectorMock.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        var dataProtectionMock = new Mock<IDataProtectionProvider>();
        dataProtectionMock.Setup(dp => dp.CreateProtector(It.IsAny<string>())).Returns(protectorMock.Object);
        var svc = new NodeMetadataService(db, cache, mediator, nodeSecurity, dataProtectionMock.Object);
        return (svc, db);
    }

    private static MSOSync.Persistence.Entities.SyncNode MakeNode(string id, string groupId = "g1") => new()
    {
        NodeId         = id,
        GroupId        = groupId,
        SyncUrl        = "http://localhost",
        LifecycleState = NodeLifecycleState.Active,
    };

    [Fact]
    public async Task GetNodesCursor_FirstPage_ReturnsCorrectItems()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 10; i++)
            db.Nodes.Add(MakeNode($"node-{i:D3}"));
        await db.SaveChangesAsync();

        var result = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 3, Cursor = null }, default);

        result.Items.Should().HaveCount(3);
        result.HasMore.Should().BeTrue();
        result.NextCursor.Should().NotBeNull();
        // Items ordered by node_id ASC — node-001, node-002, node-003
        result.Items[0].NodeId.Should().Be("node-001");
        result.Items[2].NodeId.Should().Be("node-003");
    }

    [Fact]
    public async Task GetNodesCursor_SubsequentPage_ContinuesFromCursor()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 5; i++)
            db.Nodes.Add(MakeNode($"node-{i:D3}"));
        await db.SaveChangesAsync();

        var page1 = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 2, Cursor = null }, default);
        var page2 = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 2, Cursor = page1.NextCursor }, default);

        page2.Items[0].NodeId.Should().Be("node-003");
        page2.Items[1].NodeId.Should().Be("node-004");
        // Ensure no overlap
        page1.Items.Select(n => n.NodeId)
            .Intersect(page2.Items.Select(n => n.NodeId))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task GetNodesCursor_TamperedCursor_ThrowsArgumentException()
    {
        var (svc, _) = Make();
        var act = async () => await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 5, Cursor = "not-a-valid-cursor" }, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetNodesCursor_ExhaustedPagination_HasMoreFalse()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(MakeNode("node-001"), MakeNode("node-002"));
        await db.SaveChangesAsync();

        var result = await svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 10, Cursor = null }, default);

        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetNodesWithGate_BelowThreshold_ReturnsFullList()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(MakeNode("node-001"), MakeNode("node-002"));
        await db.SaveChangesAsync();

        var gate = await svc.GetNodesWithGateAsync(threshold: 5, default);

        gate.PaginationRequired.Should().BeFalse();
        gate.Items.Should().NotBeNull().And.HaveCount(2);
        gate.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetNodesWithGate_AboveThreshold_ReturnsPaginationRequired()
    {
        var (svc, db) = Make();
        db.Set<MSOSync.Persistence.Entities.SyncNodeGroup>().Add(
            new MSOSync.Persistence.Entities.SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 5; i++)
            db.Nodes.Add(MakeNode($"node-{i:D3}"));
        await db.SaveChangesAsync();

        var gate = await svc.GetNodesWithGateAsync(threshold: 3, default);

        gate.PaginationRequired.Should().BeTrue();
        gate.Items.Should().BeNull();
        gate.NextCursor.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run tests — confirm they fail**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj \
  --filter "FullyQualifiedName~NodeCursorPagination" -v normal
```

Expected: compile errors (`CursorToken.EncodeString` not found, `NodeCursorFilter` not found, etc.).

- [ ] **Step 3: Add `EncodeString`/`DecodeString` to `CursorToken`**

Open `src/MSOSync.Common/Pagination/CursorToken.cs`. After the existing `Decode` method, add:

```csharp
/// <summary>
/// Encodes a cursor where the primary key is a <see cref="string"/> (e.g. node_id).
/// Format (before outer base64): v2n:{nodeIdBase64}:{ticks}:{base64Hmac}
/// </summary>
public static string EncodeString(string id, long ticks, ReadOnlySpan<byte> hmacKey)
{
    var idBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(id));
    var payload  = $"v2n:{idBase64}:{ticks}";
    var payloadBytes = Encoding.UTF8.GetBytes(payload);
    var hmac     = HMACSHA256.HashData(hmacKey, payloadBytes);
    var combined = $"{payload}:{Convert.ToBase64String(hmac)}";
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(combined));
}

/// <summary>
/// Decodes and verifies a string-keyed cursor.
/// Throws <see cref="ArgumentException"/> on any malformed or tampered token.
/// </summary>
public static (string Id, long Ticks) DecodeString(string token, ReadOnlySpan<byte> hmacKey)
{
    string raw;
    try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(token)); }
    catch { throw new ArgumentException("Invalid cursor token."); }

    var lastColon = raw.LastIndexOf(':');
    if (lastColon < 0)
        throw new ArgumentException("Invalid cursor token format.");

    var hmacBase64 = raw[(lastColon + 1)..];
    var payload    = raw[..lastColon];

    var parts = payload.Split(':');
    if (parts.Length != 3 || parts[0] != "v2n")
        throw new ArgumentException("Invalid cursor token format.");

    byte[] expectedHmac;
    try { expectedHmac = Convert.FromBase64String(hmacBase64); }
    catch { throw new ArgumentException("Invalid cursor token signature."); }

    var actualHmac = HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(payload));
    if (!CryptographicOperations.FixedTimeEquals(expectedHmac, actualHmac))
        throw new ArgumentException("Invalid cursor token signature.");

    if (!long.TryParse(parts[2], out var ticks))
        throw new ArgumentException("Invalid cursor token values.");

    string decodedId;
    try { decodedId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])); }
    catch { throw new ArgumentException("Invalid cursor token id encoding."); }

    return (decodedId, ticks);
}
```

- [ ] **Step 4: Add `EncodeString`/`DecodeString` delegates to `CursorSigner`**

Open `src/MSOSync.Metadata/Pagination/CursorSigner.cs`. After the existing `Decode` method, add:

```csharp
public string EncodeString(string id, long ticks)
    => CursorToken.EncodeString(id, ticks, _key);

public (string Id, long Ticks) DecodeString(string token)
    => CursorToken.DecodeString(token, _key);
```

- [ ] **Step 5: Create `PaginationOptions`**

Create `src/MSOSync.Metadata/Options/PaginationOptions.cs`:

```csharp
namespace MSOSync.Metadata.Options;

public sealed class PaginationOptions
{
    public const string Section = "Pagination";

    /// <summary>
    /// When the node count reaches this threshold, GET /api/v1/nodes returns a pagination-required
    /// response instead of the full list. Default: 200.
    /// </summary>
    public int NodeListCursorThreshold { get; init; } = 200;
}
```

- [ ] **Step 6: Create `NodeCursorFilter`**

Create `src/MSOSync.Metadata/NodeManagement/NodeCursorFilter.cs`:

```csharp
namespace MSOSync.Metadata.NodeManagement;

public sealed record NodeCursorFilter
{
    public string? Cursor        { get; init; }
    public int     PageSize      { get; init; } = 50;
    public bool    IncludeTotal  { get; init; } = false;

    public int ClampedPageSize => Math.Clamp(PageSize, 1, 200);
}
```

- [ ] **Step 7: Create `NodeListGateResult` (service record) and `NodeListGateResponse` (API DTO)**

`NodeListGateResult` is an internal service result — add it to `NodeCursorFilter.cs` (same file is fine since it's small):

Open `src/MSOSync.Metadata/NodeManagement/NodeCursorFilter.cs` and append:

```csharp
using MSOSync.Metadata.Dtos;

// (add at end of file, same namespace)
public sealed record NodeListGateResult(
    bool                    PaginationRequired,
    IReadOnlyList<NodeDto>? Items,
    string?                 NextCursor);
```

Create `src/MSOSync.Api/Dtos/Nodes/NodeListGateResponse.cs`:

```csharp
using MSOSync.Metadata.Dtos;

namespace MSOSync.Api.Dtos.Nodes;

public sealed record NodeListGateResponse(
    bool                  PaginationRequired,
    IReadOnlyList<NodeDto> Items,
    string?               NextCursor,
    string                CursorEndpoint);
```

- [ ] **Step 8: Add new method signatures to `INodeMetadataService`**

Open `src/MSOSync.Metadata/Interfaces/INodeMetadataService.cs`. Add after `GetNodesPagedAsync`:

```csharp
using MSOSync.Common.Pagination;
using MSOSync.Metadata.NodeManagement;

// (existing interface body — add two lines):
Task<CursorPageResult<NodeDto>> GetNodesCursorAsync(NodeCursorFilter filter, CancellationToken ct = default);
Task<NodeListGateResult>        GetNodesWithGateAsync(int threshold, CancellationToken ct = default);
```

The full file after editing:

```csharp
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Common;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.NodeManagement;

namespace MSOSync.Metadata.Interfaces;

public interface INodeMetadataService
{
    Task<IReadOnlyList<NodeDto>>    GetNodesAsync(CancellationToken ct = default);
    Task<PagedResult<NodeDto>>      GetNodesPagedAsync(int pageNumber, int pageSize, CancellationToken ct = default);
    Task<CursorPageResult<NodeDto>> GetNodesCursorAsync(NodeCursorFilter filter, CancellationToken ct = default);
    Task<NodeListGateResult>        GetNodesWithGateAsync(int threshold, CancellationToken ct = default);
    Task<NodeDto?>                  GetNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<NodeGroupDto>> GetNodeGroupsAsync(CancellationToken ct = default);
    Task<NodeDto>  UpdateNodeAsync(string nodeId, UpdateNodeRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<RegistrationRequestDto>> GetPendingRegistrationsAsync(CancellationToken ct = default);
    Task RejectRegistrationAsync(long requestId, CancellationToken ct = default);
    Task<NodeSecurityInfoDto> GetNodeSecurityInfoAsync(string nodeId, CancellationToken ct = default);
    Task RecordHeartbeatAsync(string nodeId, DateTime heartbeatTime, CancellationToken ct = default);
    Task<CreateNodeResult> CreateNodeAsync(CreateNodeRequest req, CancellationToken ct = default);
}
```

- [ ] **Step 9: Implement `GetNodesCursorAsync` and `GetNodesWithGateAsync` in `NodeMetadataService`**

Open `src/MSOSync.Metadata/Services/NodeMetadataService.cs`.

Add constructor parameter for `CursorSigner`:

```csharp
// Change constructor signature to:
public sealed class NodeMetadataService(
    AppDbContext              db,
    IMemoryCache              cache,
    IMediator                 mediator,
    NodeSecurityService       nodeSecurity,
    IDataProtectionProvider   dataProtection,
    MSOSync.Metadata.Pagination.CursorSigner cursorSigner) : INodeMetadataService
```

Add the two new methods (add after `GetNodesPagedAsync`):

```csharp
public async Task<CursorPageResult<NodeDto>> GetNodesCursorAsync(
    NodeCursorFilter filter, CancellationToken ct = default)
{
    var pageSize = filter.ClampedPageSize;
    var q = db.Nodes.AsNoTracking().OrderBy(n => n.NodeId);

    if (filter.Cursor is not null)
    {
        var (cursorNodeId, _) = cursorSigner.DecodeString(filter.Cursor);
        q = (IOrderedQueryable<SyncNode>)q.Where(
            n => string.Compare(n.NodeId, cursorNodeId, StringComparison.Ordinal) > 0);
    }

    var rows = await q
        .Take(pageSize + 1)
        .Select(n => new NodeDto(
            n.NodeId, n.GroupId, n.SyncUrl, n.LifecycleState,
            n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval,
            n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode,
            n.TransportMode, n.ConnectivityStatus, n.MaintenanceMode,
            n.DbServer, n.DbName, n.DbAuthMode, n.DbUser,
            n.DbPasswordEncrypted != null, n.AgentVersion))
        .ToListAsync(ct);

    var hasMore = rows.Count > pageSize;
    if (hasMore) rows = rows.Take(pageSize).ToList();

    string? nextCursor = hasMore
        ? cursorSigner.EncodeString(rows[^1].NodeId, DateTime.UtcNow.Ticks)
        : null;

    int? totalCount = null;
    if (filter.IncludeTotal)
        totalCount = await db.Nodes.AsNoTracking().CountAsync(ct);

    return new CursorPageResult<NodeDto>(rows.AsReadOnly(), nextCursor, hasMore, totalCount);
}

public async Task<NodeListGateResult> GetNodesWithGateAsync(
    int threshold, CancellationToken ct = default)
{
    var count = await db.Nodes.AsNoTracking().CountAsync(ct);

    if (count < threshold)
    {
        var items = await db.Nodes.AsNoTracking()
            .OrderBy(n => n.NodeId)
            .Select(n => new NodeDto(
                n.NodeId, n.GroupId, n.SyncUrl, n.LifecycleState,
                n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval,
                n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode,
                n.TransportMode, n.ConnectivityStatus, n.MaintenanceMode,
                n.DbServer, n.DbName, n.DbAuthMode, n.DbUser,
                n.DbPasswordEncrypted != null, n.AgentVersion))
            .ToListAsync(ct);
        return new NodeListGateResult(false, items.AsReadOnly(), null);
    }

    // Above threshold — return first-page cursor so caller can switch to /cursor
    var firstPage = await db.Nodes.AsNoTracking()
        .OrderBy(n => n.NodeId)
        .Take(1)
        .Select(n => n.NodeId)
        .FirstOrDefaultAsync(ct);

    // Encode a cursor pointing to "before the first node" — i.e. no preceding node,
    // so the first call to /cursor with this token returns page 1 correctly.
    // We encode an empty string as the cursor sentinel; the cursor endpoint filters
    // WHERE node_id > '' which is all nodes ordered from the start.
    var firstCursor = cursorSigner.EncodeString(string.Empty, 0L);
    return new NodeListGateResult(true, null, firstCursor);
}
```

**Note on `WHERE node_id > ''`:** SQL Server varchar comparisons treat `''` as less than any non-empty string, so `WHERE node_id > ''` returns all rows. The cursor endpoint implementation must handle the empty-string sentinel as a "start from beginning" signal by recognising `cursorNodeId == string.Empty` and skipping the filter clause. Add this guard in `GetNodesCursorAsync`:

```csharp
if (filter.Cursor is not null)
{
    var (cursorNodeId, _) = cursorSigner.DecodeString(filter.Cursor);
    if (!string.IsNullOrEmpty(cursorNodeId))
    {
        q = (IOrderedQueryable<SyncNode>)q.Where(
            n => string.Compare(n.NodeId, cursorNodeId, StringComparison.Ordinal) > 0);
    }
    // empty sentinel → no filter, start from first page
}
```

- [ ] **Step 10: Update `NodesController` — add cursor endpoint, gate logic, deprecation header**

Open `src/MSOSync.Api/Controllers/NodesController.cs`.

Replace the existing `GetNodes` method and `GetNodesPaged` method and add a new `GetNodesCursor` action:

```csharp
// Add to constructor / using section at top of file:
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Nodes;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Options;
using MSOSync.Common.Pagination;

// Change constructor:
public sealed class NodesController(
    INodeMetadataService        nodeService,
    IClock                      clock,
    INodeLifecycleService       lifecycleService,
    HeartbeatProcessor          heartbeatProcessor,
    IOptions<PaginationOptions> paginationOptions) : ControllerBase

// Replace GetNodes:
[HttpGet]
[Authorize]
[ProducesResponseType(typeof(IReadOnlyList<NodeDto>), 200)]
[ProducesResponseType(typeof(NodeListGateResponse), 200)]
public async Task<IActionResult> GetNodes(CancellationToken ct)
{
    var threshold = paginationOptions.Value.NodeListCursorThreshold;
    var gate = await nodeService.GetNodesWithGateAsync(threshold, ct);

    if (!gate.PaginationRequired)
        return Ok(gate.Items);

    return Ok(new NodeListGateResponse(
        PaginationRequired: true,
        Items: Array.Empty<NodeDto>(),
        NextCursor: gate.NextCursor,
        CursorEndpoint: "/api/v1/nodes/cursor"));
}

// Replace GetNodesPaged — add Deprecation header:
[HttpGet("paged")]
[Authorize]
[ProducesResponseType(typeof(PagedResponse<NodeDto>), 200)]
public async Task<IActionResult> GetNodesPaged(
    [FromQuery] int pageNumber = 1,
    [FromQuery] int pageSize   = 50,
    CancellationToken ct = default)
{
    Response.Headers.Append("Deprecation", "true");
    Response.Headers.Append("Link", "</api/v1/nodes/cursor>; rel=\"successor-version\"");

    pageSize   = Math.Min(pageSize, 200);
    pageNumber = Math.Max(1, pageNumber);
    var result = await nodeService.GetNodesPagedAsync(pageNumber, pageSize, ct);
    var totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize);
    return Ok(new PagedResponse<NodeDto>(result.Items, result.TotalCount, result.Page, result.PageSize, totalPages));
}

// New cursor endpoint:
[HttpGet("cursor")]
[Authorize]
[ProducesResponseType(typeof(CursorPageResult<NodeDto>), 200)]
[ProducesResponseType(400)]
public async Task<IActionResult> GetNodesCursor(
    [FromQuery] string? cursor       = null,
    [FromQuery] int     pageSize     = 50,
    [FromQuery] bool    includeTotal = false,
    CancellationToken ct = default)
{
    pageSize = Math.Clamp(pageSize, 1, 200);

    try
    {
        var result = await nodeService.GetNodesCursorAsync(
            new NodeCursorFilter
            {
                Cursor       = cursor,
                PageSize     = pageSize,
                IncludeTotal = includeTotal,
            }, ct);

        return Ok(result);
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { error = "InvalidCursorToken", message = ex.Message });
    }
}
```

- [ ] **Step 11: Register `PaginationOptions` in `MetadataServiceExtensions`**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`. Locate the `// Epic 12C.0 — Cursor HMAC signing` block and add immediately before it:

```csharp
// Phase 2D.4 — Pagination options
services.Configure<PaginationOptions>(configuration.GetSection(PaginationOptions.Section));
```

- [ ] **Step 12: Add `NodeListCursorThreshold` to `appsettings.json`**

Open `src/MSOSync.App/appsettings.json`. In the `"Pagination"` section, add `NodeListCursorThreshold`:

```json
"Pagination": {
  "CursorHmacKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
  "NodeListCursorThreshold": 200
},
```

- [ ] **Step 13: Run tests — confirm they pass**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj \
  --filter "FullyQualifiedName~NodeCursorPagination" -v normal
```

Expected output: `9 passed`.

- [ ] **Step 14: Run full MetadataTests to confirm no regressions**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj -v normal
```

Expected: all existing tests pass.

- [ ] **Step 15: Commit**

```
git add src/MSOSync.Common/Pagination/CursorToken.cs \
        src/MSOSync.Metadata/Pagination/CursorSigner.cs \
        src/MSOSync.Metadata/Options/PaginationOptions.cs \
        src/MSOSync.Metadata/NodeManagement/NodeCursorFilter.cs \
        src/MSOSync.Api/Dtos/Nodes/NodeListGateResponse.cs \
        src/MSOSync.Metadata/Interfaces/INodeMetadataService.cs \
        src/MSOSync.Metadata/Services/NodeMetadataService.cs \
        src/MSOSync.Api/Controllers/NodesController.cs \
        src/MSOSync.Metadata/MetadataServiceExtensions.cs \
        src/MSOSync.App/appsettings.json \
        tests/MSOSync.MetadataTests/Scale/NodeCursorPaginationTests.cs
git commit -m "feat(2D.4-T2): add node cursor pagination, CursorToken.EncodeString, threshold gate, Deprecation header"
```
