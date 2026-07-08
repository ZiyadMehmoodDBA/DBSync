# Epic 12C Task 10: CorrelationTimelineAssembler + AuditController Correlation Endpoints

**Goal:** Build a correlation timeline view that groups all audit events sharing the same correlation ID into ordered phases, detects failed workflows, extracts entity chips, and exposes three REST endpoints: `GET /audit/correlation/{id}`, `GET /audit/correlation/search`, and `GET /audit/correlation/{id}/export`.

**Prerequisites:** The existing `AuditController` and `AuditQueryService` must be in place. `AppDbContext` with `Audits` (or equivalent) and `Operations` DbSets must be available.

---

## Step 1: Create CorrelationTimelineDto.cs

- [ ] Create file `src/MSOSync.Metadata/Audit/CorrelationTimelineDto.cs`
- [ ] Paste the following content exactly:

```csharp
namespace MSOSync.Metadata.Audit;

public sealed record CorrelationTimelineDto(
    string CorrelationId,
    Guid? OperationId,
    string? OperationType,
    string? OperationStatus,
    string? OperationResult,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    TimeSpan? Duration,
    string? InitiatedBy,
    EntityChipDto[] EntityChips,
    int TotalEventCount,
    bool IsFailedWorkflow,
    string? FailureSummary,
    CorrelationPhaseDto[] Phases);

public sealed record CorrelationPhaseDto(
    string PhaseName,
    CorrelationEventDto[] Events);

public sealed record CorrelationEventDto(
    long AuditId,
    DateTime OccurredAt,
    TimeSpan? DurationSincePrevious,
    string ActionName,
    string Summary,
    string? ActorUsername,
    string Category,
    string Severity,
    string? EntityType,
    string? EntityId,
    string? DeepLink);

public sealed record EntityChipDto(
    string Type,
    string Label,
    string DeepLink);

public sealed record CorrelationSearchResult(
    string CorrelationId,
    string? OperationType,
    int EventCount);
```

---

## Step 2: Confirm audit entity field names

Before writing the assembler, confirm the exact property names on your audit entity. Run a search to find the entity class:

- [ ] Search for the class that maps to the `sync_audit` table (e.g., a file named `AuditLog.cs`, `SyncAudit.cs`, or `Audit.cs` in the Infrastructure or Domain layer)
- [ ] Note the exact property names for: primary key, action name, description/summary, actor/username, entity type, entity ID, correlation ID, create time, severity (if present)
- [ ] The assembler below uses these assumed names — replace them with the actual names found:

| Assumed name | Replace with actual |
|---|---|
| `a.AuditId` | actual PK property |
| `a.ActionName` | actual action/event name property |
| `a.Description` | actual summary/message property |
| `a.ActorUsername` | actual actor property |
| `a.EntityType` | actual entity type property |
| `a.EntityId` | actual entity ID property |
| `a.CorrelationId` | actual correlation property |
| `a.CreateTime` | actual timestamp property |
| `a.Severity` | actual severity property (may not exist; derive from action name if absent) |

---

## Step 3: Create CorrelationTimelineAssembler.cs

- [ ] Create file `src/MSOSync.Metadata/Audit/CorrelationTimelineAssembler.cs`
- [ ] Paste the following content and replace property names marked with `// ADJUST`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Infrastructure.Persistence;   // ADJUST: actual DbContext namespace

namespace MSOSync.Metadata.Audit;

public sealed class CorrelationTimelineAssembler(AppDbContext db)  // ADJUST: actual DbContext type
{
    // Phase name assignment — order matters: more specific prefixes checked first
    private static readonly (string Prefix, string Phase)[] PhaseMap =
    [
        ("NODE_REGISTR", "Registration"),
        ("NODE_APPROVED", "Registration"),
        ("NODE_REJECTED", "Registration"),
        ("NODE_", "Lifecycle"),
        ("BOOTSTRAP_", "Lifecycle"),
        ("CONFIGURATION_", "Configuration"),
        ("ROLLOUT_", "Configuration"),
        ("HEARTBEAT_", "Configuration"),
        ("EXPORT_", "Operations"),
        ("AUTH_", "Security"),
        ("TOKEN_", "Security"),
    ];

    private static readonly string[] PhaseOrder =
        ["Registration", "Lifecycle", "Configuration", "Operations", "Security", "System"];

    public async Task<CorrelationTimelineDto?> AssembleAsync(
        string correlationId, CancellationToken ct)
    {
        // Step 1: Load all audit events for this correlation ID
        var auditRows = await db.AuditLogs             // ADJUST: actual DbSet name
            .AsNoTracking()
            .Where(a => a.CorrelationId == correlationId)   // ADJUST: property name
            .OrderBy(a => a.CreateTime)                     // ADJUST: property name
            .ToListAsync(ct);

        if (auditRows.Count == 0)
            return null;

        // Step 2: Load the associated operation if any
        var operation = await db.Operations            // ADJUST: actual DbSet name
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.CorrelationId == correlationId, ct);  // ADJUST: property name

        // Step 3: Map audit rows to CorrelationEventDto
        DateTime? prevTime = null;
        var events = auditRows.Select(a =>
        {
            var occurredAt = a.CreateTime;             // ADJUST
            var durationSince = prevTime.HasValue
                ? occurredAt - prevTime.Value
                : (TimeSpan?)null;
            prevTime = occurredAt;

            var actionName = a.ActionName ?? "";       // ADJUST
            var category = DeriveCategory(actionName);
            var severity = DeriveSeverity(actionName, a.Severity);  // ADJUST: a.Severity may not exist
            var deepLink = DeriveDeepLink(
                actionName,
                a.EntityId?.ToString(),                // ADJUST
                operation?.Id);                        // ADJUST: operation PK property

            return new CorrelationEventDto(
                AuditId: a.AuditId,                    // ADJUST
                OccurredAt: occurredAt,
                DurationSincePrevious: durationSince,
                ActionName: actionName,
                Summary: a.Description ?? actionName,  // ADJUST
                ActorUsername: a.ActorUsername,         // ADJUST
                Category: category,
                Severity: severity,
                EntityType: a.EntityType,               // ADJUST
                EntityId: a.EntityId?.ToString(),       // ADJUST
                DeepLink: deepLink);
        }).ToArray();

        // Step 4: Group into phases, in defined order
        var grouped = events
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).ToArray());

        var phases = PhaseOrder
            .Where(p => grouped.ContainsKey(p))
            .Select(p => new CorrelationPhaseDto(p, grouped[p]))
            .ToArray();

        // Also include any categories not in PhaseOrder
        var knownPhases = new HashSet<string>(PhaseOrder);
        var extraPhases = grouped.Keys
            .Where(k => !knownPhases.Contains(k))
            .Select(k => new CorrelationPhaseDto(k, grouped[k]))
            .ToArray();
        if (extraPhases.Length > 0)
            phases = [.. phases, .. extraPhases];

        // Step 5: Extract unique entity chips
        var chips = events
            .Where(e => e.EntityType is not null && e.EntityId is not null)
            .GroupBy(e => (e.EntityType!, e.EntityId!))
            .Select(g =>
            {
                var (entityType, entityId) = g.Key;
                var deepLink = entityType switch
                {
                    "Node" => $"/operations/nodes/{entityId}",
                    "ConfigurationTemplate" => $"/configuration/templates/{entityId}",
                    "Operation" => $"/operations/jobs/{entityId}",
                    _ => $"/{entityType.ToLower()}s/{entityId}"
                };
                return new EntityChipDto(entityType, $"{entityType}:{entityId}", deepLink);
            })
            .ToArray();

        // Step 6: Determine failed workflow
        var lastEvent = events[^1];
        var isFailedWorkflow = lastEvent.Severity is "Error" or "Critical"
            || operation?.Result is "Failure" or "Cancelled";  // ADJUST: operation result property

        string? failureSummary = null;
        if (isFailedWorkflow)
            failureSummary = lastEvent.Summary;

        // Step 7: Compute timing
        var firstOccurred = events[0].OccurredAt;
        var lastOccurred = events[^1].OccurredAt;
        var duration = lastOccurred - firstOccurred;

        return new CorrelationTimelineDto(
            CorrelationId: correlationId,
            OperationId: operation?.Id,                     // ADJUST: operation PK (Guid)
            OperationType: operation?.OperationType,        // ADJUST
            OperationStatus: operation?.Status,             // ADJUST
            OperationResult: operation?.Result,             // ADJUST
            StartedAt: firstOccurred,
            CompletedAt: lastOccurred,
            Duration: duration,
            InitiatedBy: events.FirstOrDefault(e => e.ActorUsername is not null)?.ActorUsername,
            EntityChips: chips,
            TotalEventCount: events.Length,
            IsFailedWorkflow: isFailedWorkflow,
            FailureSummary: failureSummary,
            Phases: phases);
    }

    public async Task<CorrelationSearchResult[]> SearchAsync(
        string? nodeId, string? operationId, string? templateId,
        string? userId, string? correlationIdFilter, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();    // ADJUST: DbSet name

        if (!string.IsNullOrWhiteSpace(nodeId))
            query = query.Where(a => a.EntityId == nodeId      // ADJUST
                                  && a.EntityType == "Node");  // ADJUST

        if (!string.IsNullOrWhiteSpace(templateId))
            query = query.Where(a => a.EntityId == templateId     // ADJUST
                                  && a.EntityType == "ConfigurationTemplate");  // ADJUST

        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(a => a.ActorUsername == userId);   // ADJUST

        if (!string.IsNullOrWhiteSpace(correlationIdFilter))
            query = query.Where(a => a.CorrelationId!.StartsWith(correlationIdFilter));  // ADJUST

        if (!string.IsNullOrWhiteSpace(operationId))
        {
            // Join via operations table
            var correlationsFromOp = await db.Operations   // ADJUST
                .AsNoTracking()
                .Where(o => o.Id.ToString() == operationId || o.CorrelationId == operationId)  // ADJUST
                .Select(o => o.CorrelationId)
                .ToListAsync(ct);
            query = query.Where(a => correlationsFromOp.Contains(a.CorrelationId));  // ADJUST
        }

        var results = await query
            .Where(a => a.CorrelationId != null)   // ADJUST
            .GroupBy(a => a.CorrelationId!)        // ADJUST
            .Select(g => new { CorrelationId = g.Key, EventCount = g.Count() })
            .OrderByDescending(x => x.EventCount)
            .Take(50)
            .ToListAsync(ct);

        // Enrich with operation types
        var corrIds = results.Select(r => r.CorrelationId).ToList();
        var opTypes = await db.Operations    // ADJUST
            .AsNoTracking()
            .Where(o => corrIds.Contains(o.CorrelationId!))  // ADJUST
            .Select(o => new { o.CorrelationId, o.OperationType })  // ADJUST
            .ToListAsync(ct);
        var opTypeMap = opTypes.ToDictionary(x => x.CorrelationId!, x => x.OperationType);

        return results
            .Select(r => new CorrelationSearchResult(
                r.CorrelationId,
                opTypeMap.GetValueOrDefault(r.CorrelationId),
                r.EventCount))
            .ToArray();
    }

    private static string DeriveCategory(string actionName)
    {
        foreach (var (prefix, phase) in PhaseMap)
            if (actionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return phase;
        return "System";
    }

    private static string DeriveSeverity(string actionName, string? storedSeverity)
    {
        if (storedSeverity is not null) return storedSeverity;
        // Derive from action name when not stored
        if (actionName.Contains("FAILED") || actionName.Contains("ERROR")) return "Error";
        if (actionName.Contains("REJECTED") || actionName.Contains("DENIED")) return "Warning";
        if (actionName.Contains("DELETED") || actionName.Contains("REVOKED")) return "Warning";
        return "Info";
    }

    private static string? DeriveDeepLink(string actionName, string? entityId, Guid? operationId)
        => actionName switch
        {
            var a when a.StartsWith("NODE_") && entityId is not null
                => $"/operations/nodes/{entityId}",
            var a when a.StartsWith("ROLLOUT_") && operationId.HasValue
                => $"/operations/jobs/{operationId}",
            var a when a.StartsWith("CONFIGURATION_") && entityId is not null
                => $"/configuration/templates/{entityId}",
            var a when a.StartsWith("EXPORT_") && operationId.HasValue
                => $"/operations/jobs/{operationId}",
            _ => null
        };
}
```

---

## Step 4: Register CorrelationTimelineAssembler

- [ ] Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- [ ] Add the following registration after the existing audit registrations:

```csharp
// --- Epic 12C: Correlation Timeline ---
services.AddScoped<CorrelationTimelineAssembler>();
```

---

## Step 5: Add correlation endpoints to AuditController

- [ ] Open `src/MSOSync.Api/Controllers/AuditController.cs`
- [ ] Add `CorrelationTimelineAssembler assembler` to the controller's primary constructor. The current constructor (abbreviated) probably looks like:

```csharp
public sealed class AuditController(IAuditQueryService auditSvc) : ControllerBase
```

Change it to:

```csharp
public sealed class AuditController(
    IAuditQueryService auditSvc,
    CorrelationTimelineAssembler assembler) : ControllerBase
```

- [ ] Add using directive at the top:

```csharp
using MSOSync.Metadata.Audit;
```

- [ ] Add the following three action methods to `AuditController`:

```csharp
// GET /api/v1/audit/correlation/{correlationId}
[HttpGet("correlation/{correlationId}")]
[ProducesResponseType<CorrelationTimelineDto>(200)]
[ProducesResponseType(404)]
public async Task<IActionResult> GetCorrelationAsync(
    string correlationId, CancellationToken ct)
{
    var result = await assembler.AssembleAsync(correlationId, ct);
    return result is null ? NotFound() : Ok(result);
}

// GET /api/v1/audit/correlation/search?nodeId=...&operationId=...
[HttpGet("correlation/search")]
[ProducesResponseType<CorrelationSearchResult[]>(200)]
public async Task<IActionResult> SearchCorrelationsAsync(
    [FromQuery] string? nodeId,
    [FromQuery] string? operationId,
    [FromQuery] string? templateId,
    [FromQuery] string? userId,
    [FromQuery] string? correlationId,
    CancellationToken ct)
{
    var results = await assembler.SearchAsync(
        nodeId, operationId, templateId, userId, correlationId, ct);
    return Ok(results);
}

// GET /api/v1/audit/correlation/{correlationId}/export?format=json|markdown
[HttpGet("correlation/{correlationId}/export")]
[ProducesResponseType(200)]
[ProducesResponseType(404)]
[ProducesResponseType(501)]
public async Task<IActionResult> ExportCorrelationAsync(
    string correlationId,
    [FromQuery] string format = "json",
    CancellationToken ct = default)
{
    var timeline = await assembler.AssembleAsync(correlationId, ct);
    if (timeline is null) return NotFound();

    return format.ToLowerInvariant() switch
    {
        "json" => Ok(timeline),

        "markdown" => Content(BuildMarkdown(timeline), "text/markdown"),

        "pdf" => StatusCode(501, new { message = "PDF export is not implemented in 12C. Planned for a future release." }),

        _ => BadRequest(new { message = $"Unsupported export format '{format}'. Supported: json, markdown." })
    };
}

private static string BuildMarkdown(CorrelationTimelineDto t)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"# Correlation Timeline: {t.CorrelationId}");
    sb.AppendLine();
    sb.AppendLine($"- **Started:** {t.StartedAt:O}");
    sb.AppendLine($"- **Completed:** {t.CompletedAt:O}");
    sb.AppendLine($"- **Duration:** {t.Duration}");
    sb.AppendLine($"- **Operation Type:** {t.OperationType ?? "N/A"}");
    sb.AppendLine($"- **Status:** {t.OperationStatus ?? "N/A"}");
    sb.AppendLine($"- **Initiated By:** {t.InitiatedBy ?? "System"}");
    if (t.IsFailedWorkflow)
    {
        sb.AppendLine();
        sb.AppendLine($"> **Failed Workflow:** {t.FailureSummary}");
    }
    sb.AppendLine();
    sb.AppendLine("## Entities");
    foreach (var chip in t.EntityChips)
        sb.AppendLine($"- [{chip.Label}]({chip.DeepLink})");
    sb.AppendLine();
    foreach (var phase in t.Phases)
    {
        sb.AppendLine($"## Phase: {phase.PhaseName}");
        sb.AppendLine();
        sb.AppendLine("| Time | Action | Actor | Severity | Summary |");
        sb.AppendLine("|------|--------|-------|----------|---------|");
        foreach (var e in phase.Events)
            sb.AppendLine($"| {e.OccurredAt:HH:mm:ss.fff} | {e.ActionName} | {e.ActorUsername ?? "system"} | {e.Severity} | {e.Summary} |");
        sb.AppendLine();
    }
    return sb.ToString();
}
```

---

## Step 6: Build the solution

- [ ] Run `dotnet build MSOSync.sln`
- [ ] Expect 0 errors. Common issues:
  - `operation?.Result` may be the wrong property name — check the Operation entity
  - `operation?.Id` may need to be `operation?.OperationId` if the PK is not named `Id`
  - `db.AuditLogs` may be `db.SyncAudits` or `db.Audits` depending on the DbContext

---

## Step 7: Write unit tests for CorrelationTimelineAssembler

Because `CorrelationTimelineAssembler` requires `AppDbContext`, use an in-memory EF Core database.

- [ ] Create `tests/MSOSync.AppTests/Audit/CorrelationTimelineAssemblerTests.cs`
- [ ] Paste the following content. Adjust entity seeding to match actual entity type names.

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Infrastructure.Persistence;       // ADJUST
using MSOSync.Metadata.Audit;
using Xunit;

namespace MSOSync.AppTests.Audit;

public sealed class CorrelationTimelineAssemblerTests : IDisposable
{
    private readonly AppDbContext _db;           // ADJUST

    public CorrelationTimelineAssemblerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()  // ADJUST
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);         // ADJUST: pass required args
    }

    // Helper: create a minimal audit log entity and add to context
    // ADJUST property names to match the actual audit entity
    private object MakeAudit(string correlationId, string action, string? entityType = null,
        string? entityId = null, string? severity = null, DateTime? at = null)
    {
        // ADJUST: replace AuditLog with the actual entity class
        // return new AuditLog
        // {
        //     CorrelationId = correlationId,
        //     ActionName = action,
        //     EntityType = entityType,
        //     EntityId = entityId,
        //     Severity = severity,
        //     CreateTime = at ?? DateTime.UtcNow,
        //     Description = $"Description of {action}"
        // };
        throw new NotImplementedException("Replace this stub with actual entity construction.");
    }

    // Test 1: Empty correlationId (no rows) => returns null
    [Fact]
    public async Task AssembleAsync_UnknownCorrelationId_ReturnsNull()
    {
        var assembler = new CorrelationTimelineAssembler(_db);
        var result = await assembler.AssembleAsync("no-such-correlation-id", CancellationToken.None);
        Assert.Null(result);
    }

    // Test 2: Events in 3 phases => phases grouped correctly
    [Fact]
    public async Task AssembleAsync_EventsInThreePhases_PhasesGroupedCorrectly()
    {
        const string corrId = "corr-phases-test";
        var now = DateTime.UtcNow;

        // ADJUST: replace with actual entity seeding
        // _db.AuditLogs.AddRange(
        //     MakeAudit(corrId, "NODE_REGISTRATION_REQUESTED", at: now.AddSeconds(-3)),
        //     MakeAudit(corrId, "NODE_ACTIVATED", at: now.AddSeconds(-2)),
        //     MakeAudit(corrId, "CONFIGURATION_APPLIED", at: now.AddSeconds(-1)));
        // await _db.SaveChangesAsync();

        // Uncomment once seeding is in place:
        // var assembler = new CorrelationTimelineAssembler(_db);
        // var result = await assembler.AssembleAsync(corrId, CancellationToken.None);
        // Assert.NotNull(result);
        // Assert.Contains(result!.Phases, p => p.PhaseName == "Registration");
        // Assert.Contains(result.Phases, p => p.PhaseName == "Lifecycle");
        // Assert.Contains(result.Phases, p => p.PhaseName == "Configuration");

        // Placeholder until entity seeding is implemented:
        Assert.True(true, "Implement seeding with actual audit entity then remove this line.");
    }

    // Test 3: Last event Severity = Error => IsFailedWorkflow = true
    [Fact]
    public async Task AssembleAsync_LastEventIsError_IsFailedWorkflowTrue()
    {
        const string corrId = "corr-failed-test";
        var now = DateTime.UtcNow;

        // ADJUST: seed one event with severity "Error" as the last event
        // _db.AuditLogs.AddRange(
        //     MakeAudit(corrId, "NODE_ACTIVATED", severity: "Info", at: now.AddSeconds(-1)),
        //     MakeAudit(corrId, "NODE_ACTIVATION_FAILED", severity: "Error", at: now));
        // await _db.SaveChangesAsync();

        // Uncomment once seeding is in place:
        // var assembler = new CorrelationTimelineAssembler(_db);
        // var result = await assembler.AssembleAsync(corrId, CancellationToken.None);
        // Assert.True(result!.IsFailedWorkflow);

        Assert.True(true, "Implement seeding with actual audit entity then remove this line.");
    }

    // Test 4: Entity chips extracted from 2 nodes + 1 template
    [Fact]
    public async Task AssembleAsync_TwoNodesOneTemplate_ExtractsThreeChips()
    {
        const string corrId = "corr-chips-test";
        var now = DateTime.UtcNow;

        // ADJUST: seed events referencing 2 distinct nodes and 1 template
        // _db.AuditLogs.AddRange(
        //     MakeAudit(corrId, "NODE_ACTIVATED", entityType: "Node", entityId: "node-1", at: now.AddSeconds(-2)),
        //     MakeAudit(corrId, "NODE_ACTIVATED", entityType: "Node", entityId: "node-2", at: now.AddSeconds(-1)),
        //     MakeAudit(corrId, "CONFIGURATION_APPLIED", entityType: "ConfigurationTemplate", entityId: "tpl-1", at: now));
        // await _db.SaveChangesAsync();

        // Uncomment once seeding is in place:
        // var assembler = new CorrelationTimelineAssembler(_db);
        // var result = await assembler.AssembleAsync(corrId, CancellationToken.None);
        // Assert.Equal(3, result!.EntityChips.Length);
        // Assert.Contains(result.EntityChips, c => c.Type == "Node" && c.Label.Contains("node-1"));
        // Assert.Contains(result.EntityChips, c => c.Type == "ConfigurationTemplate");

        Assert.True(true, "Implement seeding with actual audit entity then remove this line.");
    }

    // Test 5: DurationSincePrevious is computed correctly
    [Fact]
    public async Task AssembleAsync_TwoEvents_DurationSincePreviousIsCorrect()
    {
        const string corrId = "corr-duration-test";
        var t1 = DateTime.UtcNow.AddSeconds(-10);
        var t2 = t1.AddSeconds(4);

        // ADJUST: seed two events with t1 and t2 timestamps
        // _db.AuditLogs.AddRange(
        //     MakeAudit(corrId, "NODE_ACTIVATED", at: t1),
        //     MakeAudit(corrId, "CONFIGURATION_APPLIED", at: t2));
        // await _db.SaveChangesAsync();

        // Uncomment once seeding is in place:
        // var assembler = new CorrelationTimelineAssembler(_db);
        // var result = await assembler.AssembleAsync(corrId, CancellationToken.None);
        // var allEvents = result!.Phases.SelectMany(p => p.Events).OrderBy(e => e.OccurredAt).ToArray();
        // Assert.Null(allEvents[0].DurationSincePrevious);
        // Assert.Equal(TimeSpan.FromSeconds(4), allEvents[1].DurationSincePrevious!.Value, TimeSpan.FromMilliseconds(50));

        Assert.True(true, "Implement seeding with actual audit entity then remove this line.");
    }

    public void Dispose() => _db.Dispose();
}
```

**Note:** Tests 2–5 contain stubs that require entity seeding. After confirming the actual audit entity type and property names (from Step 2 of this task), replace the stubs with real code and remove the `Assert.True(true, ...)` placeholders.

- [ ] Run `dotnet test tests/MSOSync.AppTests/MSOSync.AppTests.csproj` — expect Test 1 and the stubs (all Assert.True(true)) to pass immediately. Implement stubs after confirming entity shape.

---

## Acceptance criteria

- `GET /api/v1/audit/correlation/{id}` returns 200 with timeline or 404 if not found
- `GET /api/v1/audit/correlation/search` returns up to 50 `CorrelationSearchResult` entries
- `GET /api/v1/audit/correlation/{id}/export?format=json` returns JSON
- `GET /api/v1/audit/correlation/{id}/export?format=markdown` returns `text/markdown`
- `GET /api/v1/audit/correlation/{id}/export?format=pdf` returns 501 Not Implemented
- Events are grouped into phases in the order: Registration, Lifecycle, Configuration, Operations, Security, System
- `IsFailedWorkflow = true` when last event severity is Error or Critical
- Entity chips deduplicated by (EntityType + EntityId) pair
- All 5 tests pass (stubs become real tests after entity shape confirmed)
- `dotnet build MSOSync.sln` produces 0 errors
