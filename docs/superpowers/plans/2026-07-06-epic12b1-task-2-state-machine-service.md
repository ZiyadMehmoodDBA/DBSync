# Epic 12B-1 Task 2: State Machine + Lifecycle Service + Tokens + History

> Task 2 of 7. Master plan: `2026-07-06-epic12b1-node-lifecycle-engine.md`. Spec §2.5, §4, §7.4, §9. Global Constraints apply. Requires Task 1 complete (enums, entities, M022, policies exist).

**Goal:** Build the pure `NodeLifecycleStateMachine`, the command pipeline in `NodeLifecycleService` (only lifecycle mutation gateway), the bootstrap-token service, credential revocation, `NodeLifecycleHistoryService`, lifecycle events, the transition-error model — and delete the legacy approval path.

**Files:**
- Create: `src/MSOSync.Metadata/Lifecycle/INodeLifecycleStateMachine.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/NodeLifecycleStateMachine.cs`
- Create: `src/MSOSync.Common/Exceptions/InvalidLifecycleTransitionException.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/LifecycleOptions.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/NodeLifecycleLockRegistry.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/IBootstrapTokenService.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/BootstrapTokenService.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/INodeLifecycleHistoryService.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/NodeLifecycleHistoryService.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/LifecycleDtos.cs`
- Create: `src/MSOSync.Metadata/Events/NodeLifecycleChangedEvent.cs`
- Create: `src/MSOSync.Metadata/Events/NodeMaintenanceChangedEvent.cs`
- Modify: `src/MSOSync.Metadata/NodeManagement/INodeLifecycleService.cs` + `NodeLifecycleService.cs` (extend into the gateway)
- Modify: `src/MSOSync.Metadata/NodeManagement/NodeManagementAuditActions.cs` (14 new constants)
- Modify: `src/MSOSync.Security/NodeSecurityService.cs` (add `RevokeAsync`)
- Modify: `src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs` (transition-error body)
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs` (DI)
- Modify: `src/MSOSync.Metadata/Services/NodeMetadataService.cs` + its interface (delete `ApproveRegistrationAsync`, `EnableNodeAsync`, `DisableNodeAsync`)
- Modify: `src/MSOSync.Api/Controllers/NodesController.cs` (delete legacy approve + enable/disable endpoints)
- Test: `tests/MSOSync.MetadataTests/Lifecycle/NodeLifecycleStateMachineTests.cs`
- Test: `tests/MSOSync.MetadataTests/Lifecycle/NodeLifecycleServiceCommandTests.cs`
- Test: `tests/MSOSync.MetadataTests/Lifecycle/BootstrapTokenServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 enums/entities/DbSets, `IAuditService.WriteAsync(action, detail, actorUsername, ct)`, `NodeSecurityService.PrepareToken(nodeId)`, `BCryptPasswordHasher` (`Hash`/`Verify`), `IMediator`, `ConcurrencyException`.
- Produces (Tasks 3–4 rely on):
  - `INodeLifecycleStateMachine { CanTransition, AllowedTargets, Validate }`
  - `INodeLifecycleService` new members (exact signatures in Step 5)
  - `INodeLifecycleHistoryService { WriteTransitionAsync, GetTimelineAsync, GetLatestAsync, GetCurrentStateAsync }`
  - `NodeStateDto`, `LifecycleHistoryDto`, `LifecycleHistoryFilter`, `LifecycleTransitionRecord`, `ActivateResultDto`
  - `NodeLifecycleChangedEvent(NodeId, PreviousState, NewState, Trigger, CorrelationId)` : `INotification`
  - `NodeMaintenanceChangedEvent(NodeId, Enabled)` : `INotification`
  - `IBootstrapTokenService { IssueAsync, ValidateAndConsumeAsync, RevokeAllAsync }`
  - `LifecycleOptions { DecommissionGraceMinutes = 60, BootstrapTokenTtlHours = 72 }` bound to `"Lifecycle"` config section
  - Audit constants listed in Step 6

---

## Steps

- [ ] **Step 1: Write failing state machine tests (exhaustive matrix)**

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/NodeLifecycleStateMachineTests.cs
using FluentAssertions;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class NodeLifecycleStateMachineTests
{
    private readonly NodeLifecycleStateMachine _sm = new();

    // Spec §2.2 — the exhaustive allowed set. Every pair not listed here is denied.
    public static readonly HashSet<(NodeLifecycleState, NodeLifecycleState)> Allowed =
    [
        (NodeLifecycleState.PendingApproval,     NodeLifecycleState.PendingRegistration),
        (NodeLifecycleState.PendingApproval,     NodeLifecycleState.Rejected),
        (NodeLifecycleState.PendingRegistration, NodeLifecycleState.Active),
        (NodeLifecycleState.Active,              NodeLifecycleState.Disabled),
        (NodeLifecycleState.Disabled,            NodeLifecycleState.Active),
        (NodeLifecycleState.Active,              NodeLifecycleState.Recovery),
        (NodeLifecycleState.Disabled,            NodeLifecycleState.Recovery),
        (NodeLifecycleState.Recovery,            NodeLifecycleState.Active),
        (NodeLifecycleState.Recovery,            NodeLifecycleState.Disabled),  // reject → PreviousLifecycleState
        (NodeLifecycleState.PendingApproval,     NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.PendingRegistration, NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Active,              NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Recovery,            NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Disabled,            NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Decommissioning,     NodeLifecycleState.Decommissioned),
    ];

    public static TheoryData<NodeLifecycleState, NodeLifecycleState> AllPairs()
    {
        var data = new TheoryData<NodeLifecycleState, NodeLifecycleState>();
        foreach (var from in Enum.GetValues<NodeLifecycleState>())
            foreach (var to in Enum.GetValues<NodeLifecycleState>())
                if (from != to) data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_MatchesCanonicalTable(NodeLifecycleState from, NodeLifecycleState to)
        => _sm.CanTransition(from, to).Should().Be(Allowed.Contains((from, to)));

    [Theory]
    [InlineData(NodeLifecycleState.Rejected)]
    [InlineData(NodeLifecycleState.Decommissioned)]
    public void Invariant1_TerminalStates_HaveNoExits(NodeLifecycleState terminal)
        => _sm.AllowedTargets(terminal).Should().BeEmpty();

    [Fact]
    public void SelfTransition_IsDenied()
    {
        foreach (var s in Enum.GetValues<NodeLifecycleState>())
            _sm.CanTransition(s, s).Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidTransition_ThrowsWithAllowedTargets()
    {
        // Exception carries strings: MSOSync.Common must not know the Persistence enum.
        var act = () => _sm.Validate(NodeLifecycleState.Disabled, NodeLifecycleState.Rejected);
        act.Should().Throw<InvalidLifecycleTransitionException>()
            .Which.AllowedTargets.Should().BeEquivalentTo(["Active", "Recovery", "Decommissioning"]);
    }

    [Fact]
    public void Invariant5_OnboardingIntoActive_OnlyFromPendingRegistrationOrRecoveryOrDisabled()
    {
        // Only three sources may enter Active: activation (PendingRegistration, Recovery)
        // and administrative Enable (Disabled).
        var sources = Enum.GetValues<NodeLifecycleState>()
            .Where(s => _sm.CanTransition(s, NodeLifecycleState.Active));
        sources.Should().BeEquivalentTo(
        [
            NodeLifecycleState.PendingRegistration,
            NodeLifecycleState.Recovery,
            NodeLifecycleState.Disabled,
        ]);
    }
}
```

Note: `Recovery → Disabled` is in the allowed set because recovery-reject deterministically returns to `PreviousLifecycleState`, which can be `Disabled` (spec §2.2 row "Recovery → *PreviousLifecycleState*"). `Recovery → Active` covers the other reject target and recovery activation.

- [ ] **Step 2: Run to verify failure**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"; $env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~NodeLifecycleStateMachineTests" -c Debug
```

Expected: FAIL (types missing).

- [ ] **Step 3: Implement exception + state machine**

```csharp
// src/MSOSync.Common/Exceptions/InvalidLifecycleTransitionException.cs
namespace MSOSync.Common.Exceptions;

public sealed class InvalidLifecycleTransitionException(
    string from,
    string requested,
    IReadOnlyList<string> allowedTargets,
    Guid correlationId)
    : SyncException(
        $"Invalid lifecycle transition {from} -> {requested}. Allowed: {string.Join(", ", allowedTargets)}",
        "INVALID_LIFECYCLE_TRANSITION")
{
    public string From { get; } = from;
    public string Requested { get; } = requested;
    public IReadOnlyList<string> AllowedTargets { get; } = allowedTargets;
    public Guid CorrelationId { get; } = correlationId;
}
```

(If `SyncException` has a different constructor shape, match it — message + code are the two required pieces. The typed properties feed the §7.4 error body. The exception carries STRINGS because `MSOSync.Common` must not reference `MSOSync.Persistence` where the enum lives.)

```csharp
// src/MSOSync.Metadata/Lifecycle/INodeLifecycleStateMachine.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public interface INodeLifecycleStateMachine
{
    bool CanTransition(NodeLifecycleState from, NodeLifecycleState to);
    IReadOnlyList<NodeLifecycleState> AllowedTargets(NodeLifecycleState from);
    /// Throws InvalidLifecycleTransitionException when denied.
    void Validate(NodeLifecycleState from, NodeLifecycleState to, Guid correlationId = default);
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/NodeLifecycleStateMachine.cs
using MSOSync.Common.Exceptions;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// Pure domain object — no DB, no services, no logging (spec §2.5).
/// This table is the SINGLE CANONICAL AUTHORITY for transitions (spec §2.2).
public sealed class NodeLifecycleStateMachine : INodeLifecycleStateMachine
{
    private static readonly IReadOnlyDictionary<NodeLifecycleState, NodeLifecycleState[]> Transitions =
        new Dictionary<NodeLifecycleState, NodeLifecycleState[]>
        {
            [NodeLifecycleState.PendingApproval] =
                [NodeLifecycleState.PendingRegistration, NodeLifecycleState.Rejected, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.PendingRegistration] =
                [NodeLifecycleState.Active, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Active] =
                [NodeLifecycleState.Disabled, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Recovery] =
                [NodeLifecycleState.Active, NodeLifecycleState.Disabled, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Disabled] =
                [NodeLifecycleState.Active, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Decommissioning] =
                [NodeLifecycleState.Decommissioned],
            [NodeLifecycleState.Decommissioned] = [],   // terminal — Invariant 1
            [NodeLifecycleState.Rejected] = [],          // terminal — Invariant 1
        };

    public bool CanTransition(NodeLifecycleState from, NodeLifecycleState to)
        => Transitions[from].Contains(to);

    public IReadOnlyList<NodeLifecycleState> AllowedTargets(NodeLifecycleState from)
        => Transitions[from];

    public void Validate(NodeLifecycleState from, NodeLifecycleState to, Guid correlationId = default)
    {
        if (!CanTransition(from, to))
            throw new InvalidLifecycleTransitionException(
                from.ToString(), to.ToString(),
                Transitions[from].Select(t => t.ToString()).ToArray(),
                correlationId);
    }
}
```

Run the state machine tests — expected: PASS.

- [ ] **Step 4: Options, lock registry, bootstrap tokens, events, history service, DTOs**

```csharp
// src/MSOSync.Metadata/Lifecycle/LifecycleOptions.cs
namespace MSOSync.Metadata.Lifecycle;

public sealed class LifecycleOptions
{
    public const string Section = "Lifecycle";
    public int DecommissionGraceMinutes { get; init; } = 60;
    public int BootstrapTokenTtlHours { get; init; } = 72;
    public bool MaintenanceContinueProbing { get; init; } = true;
    public int ConnectivityHistoryRetentionDays { get; init; } = 30;
    public int ConnectivityEvaluatorIntervalSeconds { get; init; } = 30;
    public int DecommissionWorkerIntervalSeconds { get; init; } = 30;
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/NodeLifecycleLockRegistry.cs
using System.Collections.Concurrent;

namespace MSOSync.Metadata.Lifecycle;

/// Per-node in-process serialization (single-instance hub assumption, spec §1 non-goals).
/// RowVersion optimistic concurrency remains the cross-process guard.
public sealed class NodeLifecycleLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string nodeId, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) sem.Release();
        }
    }
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/IBootstrapTokenService.cs
namespace MSOSync.Metadata.Lifecycle;

public interface IBootstrapTokenService
{
    /// Revokes all previously active tokens for the node, issues a fresh one-time token.
    /// Returns the RAW token (only time it ever exists in memory; never logged).
    Task<string> IssueAsync(string nodeId, string issuedBy, CancellationToken ct = default);

    /// True when a live (unconsumed, unexpired, unrevoked) token matches; marks it consumed.
    /// Does NOT SaveChanges — caller commits inside its transaction.
    Task<bool> ValidateAndConsumeAsync(string nodeId, string rawToken, CancellationToken ct = default);

    /// Revokes every live token for the node (recovery approve, decommission).
    Task RevokeAllAsync(string nodeId, CancellationToken ct = default);
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/BootstrapTokenService.cs
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.Lifecycle;

public sealed class BootstrapTokenService(
    AppDbContext db,
    BCryptPasswordHasher hasher,
    IOptions<LifecycleOptions> options) : IBootstrapTokenService
{
    public async Task<string> IssueAsync(string nodeId, string issuedBy, CancellationToken ct = default)
    {
        await RevokeAllAsync(nodeId, ct);

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        db.NodeBootstrapTokens.Add(new SyncNodeBootstrapToken
        {
            NodeId = nodeId,
            TokenHash = hasher.Hash(raw),
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(options.Value.BootstrapTokenTtlHours),
            IssuedBy = issuedBy,
        });
        return raw;
    }

    public async Task<bool> ValidateAndConsumeAsync(string nodeId, string rawToken, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.NodeBootstrapTokens
            .Where(t => t.NodeId == nodeId && t.ConsumedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        var match = candidates.FirstOrDefault(t => hasher.Verify(rawToken, t.TokenHash));
        if (match is null) return false;

        match.ConsumedAt = now;
        return true;
    }

    public async Task RevokeAllAsync(string nodeId, CancellationToken ct = default)
    {
        var live = await db.NodeBootstrapTokens
            .Where(t => t.NodeId == nodeId && t.ConsumedAt == null && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in live) t.RevokedAt = DateTimeOffset.UtcNow;
    }
}
```

(If `BCryptPasswordHasher` is `internal` or in a different namespace, use the same type `NodeSecurityService` receives — check its constructor. Adjust `using` accordingly.)

In `src/MSOSync.Security/NodeSecurityService.cs`, add:

```csharp
/// Revokes the node's operational credential — the node can no longer authenticate.
/// Used by recovery approval (old identity dies before new bootstrap token is issued)
/// and by decommission (trust revoked at drain start). Does NOT SaveChanges.
public async Task RevokeAsync(string nodeId, CancellationToken ct = default)
{
    var sec = await db.NodeSecurities.FirstOrDefaultAsync(s => s.NodeId == nodeId, ct);
    if (sec is null) return;
    db.NodeSecurities.Remove(sec);
}
```

```csharp
// src/MSOSync.Metadata/Events/NodeLifecycleChangedEvent.cs
using MediatR;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Events;

public sealed record NodeLifecycleChangedEvent(
    string NodeId,
    NodeLifecycleState PreviousState,
    NodeLifecycleState NewState,
    LifecycleTrigger Trigger,
    Guid CorrelationId) : INotification;
```

```csharp
// src/MSOSync.Metadata/Events/NodeMaintenanceChangedEvent.cs
using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record NodeMaintenanceChangedEvent(string NodeId, bool Enabled) : INotification;
```

```csharp
// src/MSOSync.Metadata/Lifecycle/LifecycleDtos.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed record LifecycleTransitionRecord(
    string NodeId,
    NodeLifecycleState? FromState,
    NodeLifecycleState ToState,
    LifecycleTrigger Trigger,
    string? Reason,
    string Actor,
    Guid CorrelationId,
    string? MetadataJson = null);

public sealed record LifecycleHistoryDto(
    long HistoryId,
    string NodeId,
    NodeLifecycleState? FromState,
    NodeLifecycleState ToState,
    LifecycleTrigger Trigger,
    string? Reason,
    string Actor,
    Guid? CorrelationId,
    string? MetadataJson,
    DateTimeOffset OccurredAt);

public sealed record LifecycleHistoryFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    LifecycleTrigger? Trigger = null,
    int Page = 1,
    int PageSize = 50);

public sealed record NodeStateDto(
    string NodeId,
    NodeLifecycleState LifecycleState,
    ConnectivityStatus ConnectivityStatus,
    string? ConnectivityReason,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastProbeUtc,
    bool MaintenanceMode,
    string? MaintenanceReason,
    DateTimeOffset? MaintenanceUntil,
    bool DecommissionInProgress,
    int? DrainProgressPercent,
    DateTimeOffset? DecommissionGraceUntil);

public sealed record ActivateResultDto(
    string NodeToken,
    int HeartbeatIntervalSeconds,
    int ProbeIntervalSeconds,
    int ConfigurationVersion);
```

```csharp
// src/MSOSync.Metadata/Lifecycle/INodeLifecycleHistoryService.cs
using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.Lifecycle;

public interface INodeLifecycleHistoryService
{
    /// Called ONLY by NodeLifecycleService (Invariant 2/10). Appends, never updates.
    /// Does NOT SaveChanges — participates in the command transaction.
    Task WriteTransitionAsync(LifecycleTransitionRecord record, CancellationToken ct = default);
    Task<PagedResult<LifecycleHistoryDto>> GetTimelineAsync(string nodeId, LifecycleHistoryFilter filter, CancellationToken ct = default);
    Task<LifecycleHistoryDto?> GetLatestAsync(string nodeId, CancellationToken ct = default);
    Task<NodeStateDto> GetCurrentStateAsync(string nodeId, CancellationToken ct = default);
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/NodeLifecycleHistoryService.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed class NodeLifecycleHistoryService(AppDbContext db) : INodeLifecycleHistoryService
{
    public Task WriteTransitionAsync(LifecycleTransitionRecord r, CancellationToken ct = default)
    {
        db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId = r.NodeId,
            FromState = r.FromState,
            ToState = r.ToState,
            Trigger = r.Trigger,
            Reason = r.Reason,
            Actor = r.Actor,
            CorrelationId = r.CorrelationId,
            MetadataJson = r.MetadataJson,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        return Task.CompletedTask;
    }

    public async Task<PagedResult<LifecycleHistoryDto>> GetTimelineAsync(
        string nodeId, LifecycleHistoryFilter f, CancellationToken ct = default)
    {
        var query = db.NodeLifecycleHistories.AsNoTracking().Where(h => h.NodeId == nodeId);
        if (f.From is not null) query = query.Where(h => h.OccurredAt >= f.From);
        if (f.To is not null) query = query.Where(h => h.OccurredAt <= f.To);
        if (f.Trigger is not null) query = query.Where(h => h.Trigger == f.Trigger);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.HistoryId)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)
            .Select(h => new LifecycleHistoryDto(
                h.HistoryId, h.NodeId, h.FromState, h.ToState, h.Trigger,
                h.Reason, h.Actor, h.CorrelationId, h.MetadataJson, h.OccurredAt))
            .ToListAsync(ct);

        return new PagedResult<LifecycleHistoryDto>(items, f.Page, f.PageSize, total);
    }

    public Task<LifecycleHistoryDto?> GetLatestAsync(string nodeId, CancellationToken ct = default)
        => db.NodeLifecycleHistories.AsNoTracking()
            .Where(h => h.NodeId == nodeId)
            .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.HistoryId)
            .Select(h => new LifecycleHistoryDto(
                h.HistoryId, h.NodeId, h.FromState, h.ToState, h.Trigger,
                h.Reason, h.Actor, h.CorrelationId, h.MetadataJson, h.OccurredAt))
            .FirstOrDefaultAsync(ct);

    public async Task<NodeStateDto> GetCurrentStateAsync(string nodeId, CancellationToken ct = default)
    {
        var n = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(x => x.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");

        int? drainPercent = null;
        if (n.LifecycleState == NodeLifecycleState.Decommissioning
            && n.DecommissionInitialOpenBatches is > 0)
        {
            var openNow = await CountOpenBatchesAsync(db, nodeId, ct);
            var initial = n.DecommissionInitialOpenBatches.Value;
            drainPercent = Math.Clamp(100 - (int)Math.Round(openNow * 100.0 / initial), 0, 100);
        }

        return new NodeStateDto(
            n.NodeId, n.LifecycleState, n.ConnectivityStatus, n.ConnectivityReason?.ToString(),
            n.LastHeartbeat is null ? null : new DateTimeOffset(DateTime.SpecifyKind(n.LastHeartbeat.Value, DateTimeKind.Utc)),
            n.LastProbeTime is null ? null : new DateTimeOffset(DateTime.SpecifyKind(n.LastProbeTime.Value, DateTimeKind.Utc)),
            n.MaintenanceMode, n.MaintenanceReason, n.MaintenanceUntil,
            n.LifecycleState == NodeLifecycleState.Decommissioning,
            drainPercent, n.DecommissionGraceUntil);
    }

    /// Open = the batch is not yet acknowledged. SyncOutgoingBatch.Status is a byte;
    /// 2 = Acknowledged (terminal success) — the same "unacked" rule MetricsQueryService
    /// already uses (`b.Status != 2` at MetricsQueryService.cs:38,100,155).
    /// Shared by the drain evaluator (Task 3).
    internal static Task<int> CountOpenBatchesAsync(AppDbContext db, string nodeId, CancellationToken ct)
        => db.OutgoingBatches.CountAsync(b => b.NodeId == nodeId && b.Status != 2, ct);
}
```

- [ ] **Step 5: Extend NodeLifecycleService into the mutation gateway**

Extend `src/MSOSync.Metadata/NodeManagement/INodeLifecycleService.cs` with (keep the six existing 12A members):

```csharp
// node-facing
Task<ActivateResultDto> ActivateAsync(string externalId, string bootstrapToken, string agentVersion, CancellationToken ct = default);

// operator commands
Task EnableAsync(string nodeId, string actorUsername, CancellationToken ct = default);
Task DisableAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default);
Task StartMaintenanceAsync(string nodeId, string reason, DateTimeOffset? expectedEndAt, bool notifyNode, string actorUsername, CancellationToken ct = default);
Task EndMaintenanceAsync(string nodeId, string actorUsername, CancellationToken ct = default);
Task DecommissionAsync(string nodeId, string reason, int? gracePeriodMinutes, string actorUsername, CancellationToken ct = default);
Task ForceCompleteDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default);

// worker-only (System/Timeout trigger)
Task FinalizeDecommissionAsync(string nodeId, LifecycleTrigger trigger, string reason, CancellationToken ct = default);

// recovery (approve/reject ride the existing registration ApproveAsync/RejectAsync by RegistrationType)
```

New constructor dependencies for `NodeLifecycleService` (add to the existing primary constructor):
`INodeLifecycleStateMachine stateMachine`, `INodeLifecycleHistoryService history`, `IBootstrapTokenService bootstrapTokens`, `NodeSecurityService nodeSecurity`, `NodeLifecycleLockRegistry locks`, `IMediator mediator` (already present if 12A publishes notifications — verify), `IOptions<LifecycleOptions> options`, `ILogger<NodeLifecycleService> logger` (add if absent).

**The pipeline core** — add this private helper; every command below uses it:

```csharp
private async Task<Guid> ExecuteTransitionAsync(
    string nodeId,
    NodeLifecycleState target,
    LifecycleTrigger trigger,
    string actor,
    string? reason,
    string auditAction,
    Func<SyncNode, NodeLifecycleState, Task>? mutate = null,   // (node, previousState) — extra column writes inside the transaction
    string? metadataJson = null,
    CancellationToken ct = default)
{
    var correlationId = Guid.NewGuid();
    using var _ = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
    using var @lock = await locks.AcquireAsync(nodeId, ct);

    var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
        ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");

    // Revalidate at execution time — never trust pre-loaded state (spec §4.1)
    stateMachine.Validate(node.LifecycleState, target, correlationId);
    var previous = node.LifecycleState;

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    node.LifecycleState = target;
    if (mutate is not null) await mutate(node, previous);

    await history.WriteTransitionAsync(new LifecycleTransitionRecord(
        nodeId, previous, target, trigger, reason, actor, correlationId, metadataJson), ct);
    await auditSvc.WriteAsync(auditAction, $"node:{nodeId} {previous}->{target} corr:{correlationId}", actor, ct);

    try
    {
        await db.SaveChangesAsync(ct);   // RowVersion check happens here
    }
    catch (DbUpdateConcurrencyException)
    {
        throw new ConcurrencyException($"Node {nodeId} was modified concurrently");
    }
    await tx.CommitAsync(ct);

    // Publish AFTER commit — never before (spec §4.1)
    await mediator.Publish(new NodeLifecycleChangedEvent(nodeId, previous, target, trigger, correlationId), ct);
    return correlationId;
}
```

(SQLite unit tests: `BeginTransactionAsync` works on relational SQLite; if the 12A test `TestDbContext` uses a shared open connection, transactions work as-is. If any existing test infra breaks on explicit transactions, wrap with `db.Database.CreateExecutionStrategy()` per the codebase's existing pattern — check how other services do transactional writes first and mirror it.)

**Commands** (all in the `NodeLifecycleService` class):

```csharp
public Task EnableAsync(string nodeId, string actorUsername, CancellationToken ct = default)
    => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Active, LifecycleTrigger.Manual,
        actorUsername, null, NodeManagementAuditActions.NodeEnabled, ct: ct);

public Task DisableAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default)
    => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Disabled, LifecycleTrigger.Manual,
        actorUsername, reason, NodeManagementAuditActions.NodeDisabled, ct: ct);
```

```csharp
public async Task StartMaintenanceAsync(
    string nodeId, string reason, DateTimeOffset? expectedEndAt, bool notifyNode,
    string actorUsername, CancellationToken ct = default)
{
    var correlationId = Guid.NewGuid();
    using var @lock = await locks.AcquireAsync(nodeId, ct);

    var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
        ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");

    // Business rule (spec §4.3): maintenance settable only on Active nodes.
    if (node.LifecycleState != NodeLifecycleState.Active)
        throw new InvalidLifecycleTransitionException(
            node.LifecycleState.ToString(), "StartMaintenance",
            [NodeLifecycleState.Active.ToString()], correlationId);

    var extending = node.MaintenanceMode;   // Invariant 11: repeat = window change, audited as EXTENDED

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    node.MaintenanceMode = true;
    node.MaintenanceReason = reason;
    node.MaintenanceStartedAt ??= DateTimeOffset.UtcNow;
    node.MaintenanceUntil = expectedEndAt;
    node.MaintenanceStartedBy = actorUsername;

    // Not a lifecycle transition — history row records the maintenance event via MetadataJson
    await history.WriteTransitionAsync(new LifecycleTransitionRecord(
        nodeId, node.LifecycleState, node.LifecycleState, LifecycleTrigger.Manual,
        reason, actorUsername, correlationId,
        $$"""{"maintenance":"start","until":{{(expectedEndAt is null ? "null" : $"\"{expectedEndAt:O}\"")}},"notifyNode":{{notifyNode.ToString().ToLowerInvariant()}}}"""), ct);

    await auditSvc.WriteAsync(
        extending ? NodeManagementAuditActions.NodeMaintenanceExtended
                  : NodeManagementAuditActions.NodeMaintenanceStarted,
        $"node:{nodeId} reason:{reason} corr:{correlationId}", actorUsername, ct);

    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateConcurrencyException) { throw new ConcurrencyException($"Node {nodeId} was modified concurrently"); }
    await tx.CommitAsync(ct);

    await mediator.Publish(new NodeMaintenanceChangedEvent(nodeId, true), ct);
    // notifyNode: best-effort — Task 4 wires the outbound notification; nothing to do here yet.
}

public async Task EndMaintenanceAsync(string nodeId, string actorUsername, CancellationToken ct = default)
{
    var correlationId = Guid.NewGuid();
    using var @lock = await locks.AcquireAsync(nodeId, ct);

    var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
        ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");
    if (!node.MaintenanceMode) return;   // idempotent no-op (Invariant 11)

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    node.MaintenanceMode = false;
    node.MaintenanceReason = null;
    node.MaintenanceStartedAt = null;
    node.MaintenanceUntil = null;
    node.MaintenanceStartedBy = null;

    await history.WriteTransitionAsync(new LifecycleTransitionRecord(
        nodeId, node.LifecycleState, node.LifecycleState, LifecycleTrigger.Manual,
        null, actorUsername, correlationId, """{"maintenance":"end"}"""), ct);
    await auditSvc.WriteAsync(NodeManagementAuditActions.NodeMaintenanceEnded,
        $"node:{nodeId} corr:{correlationId}", actorUsername, ct);

    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateConcurrencyException) { throw new ConcurrencyException($"Node {nodeId} was modified concurrently"); }
    await tx.CommitAsync(ct);

    await mediator.Publish(new NodeMaintenanceChangedEvent(nodeId, false), ct);
}
```

```csharp
public async Task DecommissionAsync(
    string nodeId, string reason, int? gracePeriodMinutes, string actorUsername, CancellationToken ct = default)
{
    var grace = TimeSpan.FromMinutes(gracePeriodMinutes ?? options.Value.DecommissionGraceMinutes);
    var openBatches = await NodeLifecycleHistoryService.CountOpenBatchesAsync(db, nodeId, ct);

    await ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioning, LifecycleTrigger.Manual,
        actorUsername, reason, NodeManagementAuditActions.NodeDecommissionStarted,
        mutate: async (node, _) =>
        {
            node.DecommissionReason = reason;
            node.DecommissionStartedAt = DateTimeOffset.UtcNow;
            node.DecommissionGraceUntil = DateTimeOffset.UtcNow.Add(grace);
            node.DecommissionInitialOpenBatches = openBatches;
            // Revoke trust at drain start (spec §4.7 step 3)
            await nodeSecurity.RevokeAsync(nodeId, ct);
            await bootstrapTokens.RevokeAllAsync(nodeId, ct);
        },
        metadataJson: $$"""{"graceMinutes":{{(int)grace.TotalMinutes}},"initialOpenBatches":{{openBatches}}}""",
        ct: ct);
}

public Task ForceCompleteDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default)
    => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, LifecycleTrigger.Manual,
        actorUsername, "forced by operator", NodeManagementAuditActions.NodeDecommissionForced,
        mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);

public Task FinalizeDecommissionAsync(string nodeId, LifecycleTrigger trigger, string reason, CancellationToken ct = default)
    => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, trigger,
        "system", reason, NodeManagementAuditActions.NodeDecommissionCompleted,
        mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);

// ExternalId freed only when Decommissioned (spec §2.2 note). Preserve traceability in NodeName.
private static void FreeExternalId(SyncNode node)
{
    if (node.ExternalId.Length > 0)
    {
        node.NodeName = $"{node.NodeName} (decommissioned, was {node.ExternalId})";
        node.ExternalId = string.Empty;
    }
}
```

```csharp
public async Task<ActivateResultDto> ActivateAsync(
    string externalId, string bootstrapToken, string agentVersion, CancellationToken ct = default)
{
    var node = await db.Nodes.FirstOrDefaultAsync(n => n.ExternalId == externalId, ct)
        ?? throw new UnauthorizedException("Invalid activation credentials", "ACTIVATION_DENIED");
    // NOTE: 401 (not 404) for unknown ExternalId — do not leak node existence to unauthenticated callers.

    var correlationId = Guid.NewGuid();
    using var @lock = await locks.AcquireAsync(node.NodeId, ct);

    // Reload under lock — retry safety (Invariant 11)
    await db.Entry(node).ReloadAsync(ct);

    if (node.LifecycleState is not (NodeLifecycleState.PendingRegistration or NodeLifecycleState.Recovery))
        throw new InvalidLifecycleTransitionException(
            node.LifecycleState.ToString(), NodeLifecycleState.Active.ToString(),
            stateMachine.AllowedTargets(node.LifecycleState).Select(s => s.ToString()).ToArray(),
            correlationId);

    var previous = node.LifecycleState;
    var wasRecovery = previous == NodeLifecycleState.Recovery;

    await using var tx = await db.Database.BeginTransactionAsync(ct);

    if (!await bootstrapTokens.ValidateAndConsumeAsync(node.NodeId, bootstrapToken, ct))
        throw new UnauthorizedException("Invalid activation credentials", "ACTIVATION_DENIED");

    stateMachine.Validate(previous, NodeLifecycleState.Active, correlationId);
    node.LifecycleState = NodeLifecycleState.Active;
    node.RegistrationTime ??= DateTime.UtcNow;
    if (wasRecovery) node.PreviousLifecycleState = null;   // Invariant 4

    var credential = nodeSecurity.PrepareToken(node.NodeId);   // operational node token (existing model)

    await history.WriteTransitionAsync(new LifecycleTransitionRecord(
        node.NodeId, previous, NodeLifecycleState.Active, LifecycleTrigger.Activation,
        null, "system", correlationId,
        $$"""{"agentVersion":"{{agentVersion}}"}"""), ct);
    await auditSvc.WriteAsync(NodeManagementAuditActions.NodeActivated,
        $"node:{node.NodeId} agent:{agentVersion} corr:{correlationId}", "system", ct);
    if (wasRecovery)
        await auditSvc.WriteAsync(NodeManagementAuditActions.NodeRecoveryActivated,
            $"node:{node.NodeId} corr:{correlationId}", "system", ct);

    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateConcurrencyException) { throw new ConcurrencyException($"Node {node.NodeId} was modified concurrently"); }
    await tx.CommitAsync(ct);

    await mediator.Publish(new NodeLifecycleChangedEvent(
        node.NodeId, previous, NodeLifecycleState.Active, LifecycleTrigger.Activation, correlationId), ct);

    return new ActivateResultDto(
        credential.RawToken,
        HeartbeatIntervalSeconds: 30,
        ProbeIntervalSeconds: 60,
        ConfigurationVersion: 1);   // fixed until 12B-2 (spec §4.5)
}
```

(`NodeProvisionResult`'s raw-token property: verify the actual name — `NodeMetadataService.CreateNodeAsync` uses `provision.RawToken`; use the same. The 30/60 values: read from `IConfiguration` keys `Heartbeat:IntervalSeconds` / `Heartbeat:ProbeIntervalSeconds` with those defaults rather than literals — inject `IConfiguration` if the class doesn't already have it.)

**Recovery + registration approval rework** — modify existing methods:

1. `RegisterAsync` — after it determines `RegistrationType == Recovery` for a known ExternalId (existing logic), it must ALSO enter recovery state, via a private transition on the same pipeline:

```csharp
// inside RegisterAsync, after the SyncRegistrationRequest row is created for a Recovery request.
// Guard: duplicate re-registration (node already Recovery) is an idempotent no-op — Invariant 11.
if (registrationType == RegistrationType.Recovery
    && existingNode!.LifecycleState != NodeLifecycleState.Recovery)
{
    await ExecuteTransitionAsync(existingNode.NodeId, NodeLifecycleState.Recovery, LifecycleTrigger.Recovery,
        "system", "known ExternalId re-registered", NodeManagementAuditActions.NodeRecoveryRequested,
        mutate: (node, previous) =>
        {
            node.PreviousLifecycleState ??= previous;   // captures PRE-transition state — Invariant 4
            return Task.CompletedTask;
        }, ct: ct);
}
```

(Decommissioned ExternalIds were freed at finalization, so the lookup never matches — a returning decommissioned identity registers as `New` automatically. Decommissioning nodes: the state machine denies Decommissioning→Recovery and the command throws 409, which `RegisterAsync` surfaces.)

2. `ApproveAsync(long id, …)` — extend the existing method to dispatch by type after the current registration-row update, all inside one command flow:
   - `RegistrationType.New`: create the `SyncNode` (mirror the construction block already in `ProvisionAsync` — same fields: NodeId, ExternalId, NodeName, NodeType, GroupId, SyncUrl placeholder, Db fields from metadata) with `LifecycleState = NodeLifecycleState.PendingRegistration`, write history row (`FromState = null`, `ToState = PendingRegistration`, `Trigger = Registration`), audit stays `NODE_APPROVED`. This lands the 12A deferred item.
   - `RegistrationType.ReRegistration`: no state change (node already Active); audit `NODE_RE_REGISTERED` (existing behavior).
   - `RegistrationType.Recovery`: **no state change** (spec §2.2 note). Revoke ALL previous credentials then issue new bootstrap token: `await nodeSecurity.RevokeAsync(node.NodeId, ct); var raw = await bootstrapTokens.IssueAsync(node.NodeId, actorUsername, ct);` — audit `NODE_RECOVERY_APPROVED`. The raw token must be returned to the operator: change `ApproveAsync` return type to `Task<ApproveResultDto>` where `public sealed record ApproveResultDto(long RegistrationId, string? BootstrapToken);` (`BootstrapToken` null for non-recovery approvals). Update `NodeManagementController` approve endpoint to return it (body instead of 204 when token present) and `BulkApproveAsync` to skip Recovery rows with outcome `RequiresIndividualApproval` (bulk must never emit tokens).
3. `RejectAsync(long id, …)` — for `RegistrationType.Recovery`: transition node back deterministically:

```csharp
if (req.RegistrationType == RegistrationType.Recovery)
{
    var node = await db.Nodes.FirstAsync(n => n.NodeId == req.NodeId, ct);
    var target = node.PreviousLifecycleState
        ?? NodeLifecycleState.Disabled;   // defensive: never happens per Invariant 4
    await ExecuteTransitionAsync(node.NodeId, target, LifecycleTrigger.Recovery,
        actorUsername, reason, NodeManagementAuditActions.NodeRecoveryRejected,
        mutate: (n, _) => { n.PreviousLifecycleState = null; return Task.CompletedTask; }, ct: ct);
}
```

4. `ProvisionAsync` — direct-provision (no prior registration) keeps creating the node (`PendingRegistration` after Task 1). Change: (a) when an approve-created node with the same ExternalId already exists in `PendingRegistration`, do NOT create a second node — reuse it (spec §4.4); (b) replace the current unstored raw-token generation with `await bootstrapTokens.IssueAsync(node.NodeId, actorUsername, ct)` so activation can actually validate; (c) replace the raw `"node:provisioned"` audit string with a new constant `NodeProvisioned = "NODE_PROVISIONED"`.

**Delete legacy path:**
- `src/MSOSync.Metadata/Services/NodeMetadataService.cs`: delete `ApproveRegistrationAsync`, `EnableNodeAsync`, `DisableNodeAsync` methods + their interface members in `INodeMetadataService`.
- `src/MSOSync.Api/Controllers/NodesController.cs`: delete `POST registrations/{requestId}/approve`, `POST {id}/enable`, `POST {id}/disable` actions (their replacements arrive in Task 4's `NodeLifecycleController`; frontend migrates in Task 6).
- Fix any tests referencing the deleted members (delete tests that tested legacy approve; keep semantics-equivalent coverage — the new pipeline tests below replace them).

- [ ] **Step 6: Audit constants + GlobalExceptionHandler + DI**

`src/MSOSync.Metadata/NodeManagement/NodeManagementAuditActions.cs` — append:

```csharp
public const string NodeProvisioned            = "NODE_PROVISIONED";
public const string NodeActivated              = "NODE_ACTIVATED";
public const string NodeEnabled                = "NODE_ENABLED";
public const string NodeDisabled               = "NODE_DISABLED";
public const string NodeMaintenanceStarted     = "NODE_MAINTENANCE_STARTED";
public const string NodeMaintenanceExtended    = "NODE_MAINTENANCE_EXTENDED";
public const string NodeMaintenanceEnded       = "NODE_MAINTENANCE_ENDED";
public const string NodeDecommissionStarted    = "NODE_DECOMMISSION_STARTED";
public const string NodeDecommissionCompleted  = "NODE_DECOMMISSION_COMPLETED";
public const string NodeDecommissionForced     = "NODE_DECOMMISSION_FORCED";
public const string NodeDecommissionCancelled  = "NODE_DECOMMISSION_CANCELLED"; // reserved — not used in 12B-1
public const string NodeRecoveryRequested      = "NODE_RECOVERY_REQUESTED";
public const string NodeRecoveryApproved       = "NODE_RECOVERY_APPROVED";
public const string NodeRecoveryRejected       = "NODE_RECOVERY_REJECTED";
public const string NodeRecoveryActivated      = "NODE_RECOVERY_ACTIVATED";
```

`src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs` — the switch already maps `SyncException`-derived types; add a dedicated arm BEFORE the generic ones so the §7.4 body shape is emitted:

```csharp
InvalidLifecycleTransitionException ex => /* handled below with custom body */,
```

Implement: when the exception is `InvalidLifecycleTransitionException`, write 409 with body:

```csharp
await context.Response.WriteAsJsonAsync(new
{
    code = "INVALID_LIFECYCLE_TRANSITION",
    from = ex.From,
    requested = ex.Requested,
    allowedTransitions = ex.AllowedTargets,
    correlationId = ex.CorrelationId,
}, ct);
```

(Follow the handler's existing response-writing mechanics — status setting, content type, and the existing `correlationId` plumbing; only the body shape is new.)

`src/MSOSync.Metadata/MetadataServiceExtensions.cs` — add:

```csharp
services.Configure<LifecycleOptions>(configuration.GetSection(LifecycleOptions.Section));
services.AddSingleton<INodeLifecycleStateMachine, NodeLifecycleStateMachine>();
services.AddSingleton<NodeLifecycleLockRegistry>();
services.AddScoped<IBootstrapTokenService, BootstrapTokenService>();
services.AddScoped<INodeLifecycleHistoryService, NodeLifecycleHistoryService>();
```

(The `AddMetadata` signature discards its `IConfiguration _` parameter today — rename `_` to `configuration` to bind options.)

- [ ] **Step 7: Write service command tests (SQLite)**

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/NodeLifecycleServiceCommandTests.cs
// Use the existing TestDbContext SQLite fixture pattern from tests/MSOSync.MetadataTests.
// Mock IMediator (Moq) to capture published notifications; real state machine, real history service,
// real BootstrapTokenService with real BCryptPasswordHasher; stub IAuditService recording calls.
```

Cover — one `[Fact]` per line, names as given:

```text
Enable_FromDisabled_TransitionsToActive_WritesHistoryAndAudit_PublishesEvent
Enable_FromActive_ThrowsInvalidLifecycleTransition                       // Invariant 11 (duplicate enable → 409)
Disable_FromActive_TransitionsToDisabled
Pipeline_HistoryAuditEvent_ShareOneCorrelationId                          // capture event CorrelationId, assert history row + audit detail contain it
Activate_PendingRegistration_HappyPath_ReturnsTokenAndTransitionsActive
Activate_ConsumedToken_ThrowsUnauthorized                                 // retry safety: replay after success
Activate_RevokedToken_ThrowsUnauthorized
Activate_WrongState_Disabled_ThrowsInvalidLifecycleTransition
Activate_Recovery_ClearsPreviousLifecycleState_AuditsRecoveryActivated    // Invariant 4
Register_KnownExternalId_EntersRecovery_StoresPreviousLifecycleState      // Invariant 4
Register_AlreadyInRecovery_NoOps                                          // Invariant 11
ApproveRegistration_New_CreatesSyncNodeInPendingRegistration              // 12A deferred item
ApproveRecovery_RevokesCredentials_IssuesBootstrapToken_NoStateChange     // old NodeSecurities row gone, new bootstrap row live
RejectRecovery_ReturnsToPreviousState_ClearsIt                            // deterministic reject
StartMaintenance_OnActive_SetsColumns_AuditsStarted
StartMaintenance_Twice_AuditsExtended                                     // Invariant 11
StartMaintenance_OnDisabled_Throws
EndMaintenance_ClearsColumns_PublishesEvent
EndMaintenance_WhenNotInMaintenance_NoOps                                 // Invariant 11
Decommission_SetsGraceAndSnapshot_RevokesTrust
ForceCompleteDecommission_TransitionsToDecommissioned_FreesExternalId
FinalizeDecommission_SystemTrigger_AuditsCompleted
History_IsAppendOnly_MigrationSeedPatternWritable                         // two commands → two rows, rows never mutated
Event_PublishedOnlyAfterCommit                                            // arrange mediator mock to record; assert SaveChanges preceded Publish (sequence via callback order)
```

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/BootstrapTokenServiceTests.cs
```

```text
Issue_RevokesPriorLiveTokens
ValidateAndConsume_ValidToken_ReturnsTrue_MarksConsumed
ValidateAndConsume_ConsumedToken_ReturnsFalse
ValidateAndConsume_ExpiredToken_ReturnsFalse
ValidateAndConsume_RevokedToken_ReturnsFalse
RawToken_NeverPersisted                                                    // no stored value equals the raw token
```

- [ ] **Step 8: Run tests + build**

```pwsh
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~MSOSync.MetadataTests.Lifecycle" -c Debug
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.MetadataTests -c Debug --no-build
```

Expected: all green, zero warnings.

- [ ] **Step 9: Commit**

```pwsh
git add src/MSOSync.Metadata/Lifecycle src/MSOSync.Metadata/Events/NodeLifecycleChangedEvent.cs src/MSOSync.Metadata/Events/NodeMaintenanceChangedEvent.cs
git add src/MSOSync.Common/Exceptions/InvalidLifecycleTransitionException.cs src/MSOSync.Api/Exceptions/GlobalExceptionHandler.cs
git add src/MSOSync.Metadata/NodeManagement src/MSOSync.Metadata/Services src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.Security/NodeSecurityService.cs src/MSOSync.Api/Controllers/NodesController.cs
git add tests/MSOSync.MetadataTests
git commit -m "feat(12B-1): lifecycle state machine + command pipeline gateway, bootstrap tokens, recovery flow, legacy approval path removed"
```
