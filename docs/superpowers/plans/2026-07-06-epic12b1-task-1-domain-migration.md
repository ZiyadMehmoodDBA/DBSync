# Epic 12B-1 Task 1: Domain Model + M022 Migration + Policy Services

> Task 1 of 7. Master plan: `2026-07-06-epic12b1-node-lifecycle-engine.md`. Spec: `docs/superpowers/specs/2026-07-06-epic12b1-node-lifecycle-engine-design.md` (§2, §3, §5.2, §6). Global Constraints from the master plan apply verbatim.

**Goal:** Introduce the canonical lifecycle enums, entity/schema changes (migration `M022_NodeLifecycle`), the two pure policy services, migrate every legacy `Status`-string and `SyncEnabled` reader, delete `NodeStateMachine`/`NodeStatusWorker`, and add fail-fast startup validation — leaving the whole solution compiling green with zero warnings.

**Files:**
- Create: `src/MSOSync.Persistence/Entities/NodeLifecycleState.cs`
- Create: `src/MSOSync.Persistence/Entities/LifecycleTrigger.cs`
- Create: `src/MSOSync.Persistence/Entities/ConnectivityReason.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncNodeLifecycleHistory.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncNodeConnectivityHistory.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncNodeBootstrapToken.cs`
- Create: `src/MSOSync.Persistence/Configurations/SyncNodeLifecycleHistoryConfiguration.cs`
- Create: `src/MSOSync.Persistence/Configurations/SyncNodeConnectivityHistoryConfiguration.cs`
- Create: `src/MSOSync.Persistence/Configurations/SyncNodeBootstrapTokenConfiguration.cs`
- Create: `src/MSOSync.Persistence/LegacyStatusMap.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/INodeSyncPolicy.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/NodeSyncPolicy.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/IConnectivityPolicy.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/ConnectivityPolicy.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/LifecycleStartupValidator.cs`
- Create: `src/MSOSync.Persistence/Migrations/<timestamp>_M022_NodeLifecycle.cs` (via `dotnet ef migrations add`, then Up/Down replaced)
- Modify: `src/MSOSync.Persistence/Entities/SyncNode.cs`
- Modify: `src/MSOSync.Persistence/Configurations/SyncNodeConfiguration.cs`
- Modify: `src/MSOSync.Persistence/AppDbContext.cs` (3 DbSets)
- Modify: `src/MSOSync.Metadata/Permissions/SystemPermissions.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Modify (mechanical reader cutover — exact edits in Step 6): `src/MSOSync.Routing/RoutingService.cs`, `src/MSOSync.Scheduler/Workers/ProbeWorker.cs`, `src/MSOSync.Transport/SmartTransportService.cs`, `src/MSOSync.Topology/TopologyService.cs`, `src/MSOSync.Metadata/Topology/TopologyQueryService.cs`, `src/MSOSync.Metadata/Topology/TopologyGroupDto.cs`, `src/MSOSync.Metadata/Dtos/NodeDto.cs`, `src/MSOSync.Metadata/Services/NodeMetadataService.cs`, `src/MSOSync.Metadata/NodeManagement/NodeManagementService.cs`, `src/MSOSync.Metadata/NodeManagement/NodeLifecycleService.cs`, `src/MSOSync.Persistence/Queries/GetOfflineNodesQuery.cs`, `src/MSOSync.Api/Controllers/NodesController.cs`, `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`
- Delete: `src/MSOSync.Metadata/Nodes/INodeStateMachine.cs`, `src/MSOSync.Metadata/Nodes/NodeStateMachine.cs`, `src/MSOSync.Scheduler/Workers/NodeStatusWorker.cs`
- Test: `tests/MSOSync.MetadataTests/Lifecycle/NodeSyncPolicyTests.cs`, `tests/MSOSync.MetadataTests/Lifecycle/ConnectivityPolicyTests.cs`, `tests/MSOSync.MetadataTests/Lifecycle/LegacyStatusMapTests.cs`
- Test (fix existing): any test referencing `Status = "REGISTERED"` etc. or `SyncEnabled` (Step 8)

**Interfaces:**
- Consumes: existing `SyncNode`, `ConnectivityStatus` enum, `AppDbContext`, M018 seed pattern.
- Produces (later tasks rely on these EXACT shapes):
  - `NodeLifecycleState`, `LifecycleTrigger`, `ConnectivityReason` enums (namespace `MSOSync.Persistence.Entities`)
  - `SyncNode.LifecycleState` (enum property replacing `Status`), `SyncNode.PreviousLifecycleState`, maintenance ×5, decommission ×4, `ConnectivityReason`, `LastProbeError`, `ConsecutiveProbeFailures`, `RowVersion`
  - `db.NodeLifecycleHistories`, `db.NodeConnectivityHistories`, `db.NodeBootstrapTokens`
  - `INodeSyncPolicy { bool CanSynchronize(SyncNode); SyncEligibility Evaluate(SyncNode); }` + static `NodeSyncPolicy.EligibleExpression`
  - `IConnectivityPolicy { ConnectivityEvaluationResult Evaluate(ConnectivityTelemetry); }` with `ConnectivityTelemetry` record
  - `SystemPermissions.ManageNodeLifecycle` = `"MANAGE_NODE_LIFECYCLE"`, `SystemPermissions.ProvisionNodes` = `"PROVISION_NODES"`
  - `LegacyStatusMap.Map` (legacy string → `NodeLifecycleState`)

---

## Steps

- [ ] **Step 1: Write failing unit tests for LegacyStatusMap + policies**

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/LegacyStatusMapTests.cs
using FluentAssertions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class LegacyStatusMapTests
{
    [Theory]
    [InlineData("PENDING",     NodeLifecycleState.PendingApproval)]
    [InlineData("APPROVED",    NodeLifecycleState.PendingRegistration)]
    [InlineData("PROVISIONED", NodeLifecycleState.PendingRegistration)]
    [InlineData("REGISTERED",  NodeLifecycleState.Active)]
    [InlineData("OFFLINE",     NodeLifecycleState.Active)]
    [InlineData("DISABLED",    NodeLifecycleState.Disabled)]
    public void Map_ContainsExactlyTheSpecMapping(string legacy, NodeLifecycleState expected)
        => LegacyStatusMap.Map[legacy].Should().Be(expected);

    [Fact]
    public void Map_HasExactlySixEntries() => LegacyStatusMap.Map.Should().HaveCount(6);
}
```

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/NodeSyncPolicyTests.cs
using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class NodeSyncPolicyTests
{
    private readonly NodeSyncPolicy _policy = new();

    private static SyncNode Node(NodeLifecycleState state, bool maintenance = false) => new()
    {
        NodeId = "n1", GroupId = "g", SyncUrl = "http://x",
        LifecycleState = state, MaintenanceMode = maintenance,
    };

    [Fact]
    public void Active_NoMaintenance_CanSynchronize()
    {
        _policy.CanSynchronize(Node(NodeLifecycleState.Active)).Should().BeTrue();
        _policy.Evaluate(Node(NodeLifecycleState.Active)).Should().Be(SyncEligibility.Allowed);
    }

    [Fact]
    public void Active_InMaintenance_BlockedByMaintenance()
    {
        _policy.CanSynchronize(Node(NodeLifecycleState.Active, maintenance: true)).Should().BeFalse();
        _policy.Evaluate(Node(NodeLifecycleState.Active, maintenance: true))
            .Should().Be(SyncEligibility.BlockedByMaintenance);
    }

    [Theory]
    [InlineData(NodeLifecycleState.PendingApproval)]
    [InlineData(NodeLifecycleState.PendingRegistration)]
    [InlineData(NodeLifecycleState.Recovery)]
    [InlineData(NodeLifecycleState.Disabled)]
    [InlineData(NodeLifecycleState.Rejected)]
    public void NonActive_BlockedByLifecycle(NodeLifecycleState state)
    {
        _policy.CanSynchronize(Node(state)).Should().BeFalse();
        _policy.Evaluate(Node(state)).Should().Be(SyncEligibility.BlockedByLifecycle);
    }

    [Theory]
    [InlineData(NodeLifecycleState.Decommissioning)]
    [InlineData(NodeLifecycleState.Decommissioned)]
    public void Decommission_BlockedByDecommission(NodeLifecycleState state)
        => _policy.Evaluate(Node(state)).Should().Be(SyncEligibility.BlockedByDecommission);
}
```

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/ConnectivityPolicyTests.cs
using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class ConnectivityPolicyTests
{
    private static readonly DateTime Now = new(2026, 07, 06, 12, 00, 00, DateTimeKind.Utc);
    private static readonly TimeSpan Hb = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Pr = TimeSpan.FromSeconds(60);
    private readonly ConnectivityPolicy _policy = new();

    private static ConnectivityTelemetry T(
        NodeLifecycleState state = NodeLifecycleState.Active,
        double? hbAgeSec = 5,
        double? probeAgeSec = null,
        bool probeFailed = false,
        int consecutiveFailures = 0) => new(
            Lifecycle: state,
            LastHeartbeatUtc: hbAgeSec is null ? null : Now.AddSeconds(-hbAgeSec.Value),
            LastProbeUtc: probeAgeSec is null ? null : Now.AddSeconds(-probeAgeSec.Value),
            LastProbeFailed: probeFailed,
            ConsecutiveProbeFailures: consecutiveFailures,
            NowUtc: Now,
            HeartbeatInterval: Hb,
            ProbeInterval: Pr);

    // Rule 1 — excluded lifecycles
    [Theory]
    [InlineData(NodeLifecycleState.PendingApproval)]
    [InlineData(NodeLifecycleState.PendingRegistration)]
    [InlineData(NodeLifecycleState.Rejected)]
    [InlineData(NodeLifecycleState.Decommissioned)]
    public void ExcludedLifecycle_Unknown_NotEvaluated(NodeLifecycleState state)
        => _policy.Evaluate(T(state)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Unknown, ConnectivityReason.NotEvaluated));

    // Rule 2 — no heartbeat ever
    [Fact]
    public void NoHeartbeat_Unknown_NoHeartbeat()
        => _policy.Evaluate(T(hbAgeSec: null)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Unknown, ConnectivityReason.NoHeartbeat));

    // Rule 3 — heartbeat expired (> 3x)
    [Fact]
    public void HeartbeatOlderThan3x_Unreachable_HeartbeatExpired()
        => _policy.Evaluate(T(hbAgeSec: 91)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Unreachable, ConnectivityReason.HeartbeatExpired));

    // Rule 4 — heartbeat stale (> 1x, <= 3x)
    [Fact]
    public void HeartbeatOlderThan1x_Degraded_HeartbeatStale()
        => _policy.Evaluate(T(hbAgeSec: 45)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Degraded, ConnectivityReason.HeartbeatStale));

    // Rule 5 — fresh heartbeat + fresh failed probe
    [Fact]
    public void FreshHeartbeat_FreshFailedProbe_Degraded_ProbeFailed()
        => _policy.Evaluate(T(hbAgeSec: 5, probeAgeSec: 30, probeFailed: true, consecutiveFailures: 1)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Degraded, ConnectivityReason.ProbeFailed));

    // Rule 5 stale-probe ignore — probe older than 2x probe interval is ignored
    [Fact]
    public void FreshHeartbeat_StaleFailedProbe_Reachable_Healthy()
        => _policy.Evaluate(T(hbAgeSec: 5, probeAgeSec: 121, probeFailed: true, consecutiveFailures: 1)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Reachable, ConnectivityReason.Healthy));

    // Rule 6 — 3+ consecutive fresh failures
    [Fact]
    public void FreshHeartbeat_ThreeConsecutiveFreshFailures_Unreachable_ProbeFailures()
        => _policy.Evaluate(T(hbAgeSec: 5, probeAgeSec: 30, probeFailed: true, consecutiveFailures: 3)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Unreachable, ConnectivityReason.ProbeFailures));

    // Rule 7 — healthy
    [Fact]
    public void FreshHeartbeat_NoProbeIssues_Reachable_Healthy()
        => _policy.Evaluate(T(hbAgeSec: 5)).Should()
            .Be(new ConnectivityEvaluationResult(ConnectivityStatus.Reachable, ConnectivityReason.Healthy));

    // Invariant 9 — Recovery/Decommissioning/Disabled ARE evaluated (not excluded)
    [Theory]
    [InlineData(NodeLifecycleState.Active)]
    [InlineData(NodeLifecycleState.Recovery)]
    [InlineData(NodeLifecycleState.Disabled)]
    [InlineData(NodeLifecycleState.Decommissioning)]
    public void NonExcludedLifecycles_AreEvaluated(NodeLifecycleState state)
        => _policy.Evaluate(T(state)).Reason.Should().NotBe(ConnectivityReason.NotEvaluated);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~MSOSync.MetadataTests.Lifecycle" -c Debug
```

Expected: FAIL (compile errors — types don't exist yet).

- [ ] **Step 3: Create enums, LegacyStatusMap, and new entities**

```csharp
// src/MSOSync.Persistence/Entities/NodeLifecycleState.cs
namespace MSOSync.Persistence.Entities;

public enum NodeLifecycleState
{
    PendingApproval,      // reachable post-cutover only via migrated legacy PENDING rows
    PendingRegistration,  // SyncNode exists, awaiting /activate handshake
    Active,
    Recovery,             // identity replacement under review / awaiting re-activation
    Disabled,
    Decommissioning,      // orchestrated drain in progress
    Decommissioned,       // terminal
    Rejected,             // terminal
}
```

```csharp
// src/MSOSync.Persistence/Entities/LifecycleTrigger.cs
namespace MSOSync.Persistence.Entities;

public enum LifecycleTrigger
{
    Manual,        // operator command
    Registration,  // registration approval flow
    Activation,    // node /activate handshake
    Recovery,      // recovery flow
    System,        // worker-initiated (drain finalize on completion)
    Timeout,       // grace-period expiry
    Migration,     // M022 conversion
}
```

```csharp
// src/MSOSync.Persistence/Entities/ConnectivityReason.cs
namespace MSOSync.Persistence.Entities;

public enum ConnectivityReason
{
    NotEvaluated,
    NoHeartbeat,
    Healthy,
    HeartbeatStale,
    HeartbeatExpired,
    ProbeFailed,
    ProbeFailures,
    PendingActivation,
}
```

```csharp
// src/MSOSync.Persistence/LegacyStatusMap.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence;

/// Single source for the M022 legacy status conversion (spec §3.1).
public static class LegacyStatusMap
{
    public static readonly IReadOnlyDictionary<string, NodeLifecycleState> Map =
        new Dictionary<string, NodeLifecycleState>(StringComparer.OrdinalIgnoreCase)
        {
            ["PENDING"]     = NodeLifecycleState.PendingApproval,
            ["APPROVED"]    = NodeLifecycleState.PendingRegistration,
            ["PROVISIONED"] = NodeLifecycleState.PendingRegistration,
            ["REGISTERED"]  = NodeLifecycleState.Active,
            ["OFFLINE"]     = NodeLifecycleState.Active,
            ["DISABLED"]    = NodeLifecycleState.Disabled,
        };
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncNodeLifecycleHistory.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeLifecycleHistory
{
    public long HistoryId { get; set; }
    public string NodeId { get; set; } = null!;
    public NodeLifecycleState? FromState { get; set; }   // null = entry into canonical model
    public NodeLifecycleState ToState { get; set; }
    public LifecycleTrigger Trigger { get; set; }
    public string? Reason { get; set; }
    public string Actor { get; set; } = null!;            // username or "system"
    public Guid? CorrelationId { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncNodeConnectivityHistory.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeConnectivityHistory
{
    public long Id { get; set; }
    public string NodeId { get; set; } = null!;
    public ConnectivityStatus PreviousStatus { get; set; }
    public ConnectivityStatus NewStatus { get; set; }
    public ConnectivityReason Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncNodeBootstrapToken.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeBootstrapToken
{
    public long Id { get; set; }
    public string NodeId { get; set; } = null!;
    public string TokenHash { get; set; } = null!;         // BCrypt hash; raw token never stored
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string IssuedBy { get; set; } = null!;
}
```

- [ ] **Step 4: Modify SyncNode entity + EF configurations + AppDbContext**

Replace `src/MSOSync.Persistence/Entities/SyncNode.cs` content:

```csharp
namespace MSOSync.Persistence.Entities;

public sealed class SyncNode
{
    public string NodeId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public string SyncUrl { get; set; } = null!;
    public NodeLifecycleState LifecycleState { get; set; } = NodeLifecycleState.PendingRegistration;
    public DateTime? RegistrationTime { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public int HeartbeatInterval { get; set; } = 60;
    public TransportMode TransportMode { get; set; } = TransportMode.Pull;
    public string? UpstreamNodeId { get; set; }
    public DateTime? LastProbeTime { get; set; }
    public int? LastProbeLatencyMs { get; set; }
    public ConnectivityStatus ConnectivityStatus { get; set; } = ConnectivityStatus.Unknown;
    public ConnectivityReason? ConnectivityReason { get; set; }
    public string? LastProbeError { get; set; }
    public int ConsecutiveProbeFailures { get; set; }

    // Recovery
    public NodeLifecycleState? PreviousLifecycleState { get; set; }

    // Maintenance (orthogonal — never a lifecycle state)
    public bool MaintenanceMode { get; set; }
    public string? MaintenanceReason { get; set; }
    public DateTimeOffset? MaintenanceStartedAt { get; set; }
    public DateTimeOffset? MaintenanceUntil { get; set; }
    public string? MaintenanceStartedBy { get; set; }

    // Decommission
    public string? DecommissionReason { get; set; }
    public DateTimeOffset? DecommissionStartedAt { get; set; }
    public DateTimeOffset? DecommissionGraceUntil { get; set; }
    public int? DecommissionInitialOpenBatches { get; set; }

    // Optimistic concurrency for lifecycle commands
    public byte[] RowVersion { get; set; } = [];

    // Node classification fields (admin-provisioned)
    public string NodeType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;

    // DB connection fields (admin-provisioned)
    public string? DbServer { get; set; }
    public string? DbName { get; set; }
    public string? DbAuthMode { get; set; }  // "Windows" or "Sql"
    public string? DbUser { get; set; }
    public string? DbPasswordEncrypted { get; set; }
}
```

In `src/MSOSync.Persistence/Configurations/SyncNodeConfiguration.cs`:

Replace the `Status` and `SyncEnabled` property mappings with:

```csharp
builder.Property(e => e.LifecycleState)
    .HasColumnName("status")
    .HasColumnType("varchar(30)")
    .HasMaxLength(30)
    .IsUnicode(false)
    .HasConversion<string>()
    .IsRequired();
```

(Delete the `SyncEnabled` mapping line entirely.) Add after the `ConnectivityStatus` mapping:

```csharp
builder.Property(e => e.ConnectivityReason).HasColumnName("connectivity_reason")
    .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>();
builder.Property(e => e.LastProbeError).HasColumnName("last_probe_error")
    .HasColumnType("nvarchar(512)").HasMaxLength(512);
builder.Property(e => e.ConsecutiveProbeFailures).HasColumnName("consecutive_probe_failures")
    .HasDefaultValue(0);
builder.Property(e => e.PreviousLifecycleState).HasColumnName("previous_lifecycle_state")
    .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>();
builder.Property(e => e.MaintenanceMode).HasColumnName("maintenance_mode").HasDefaultValue(false);
builder.Property(e => e.MaintenanceReason).HasColumnName("maintenance_reason")
    .HasColumnType("nvarchar(512)").HasMaxLength(512);
builder.Property(e => e.MaintenanceStartedAt).HasColumnName("maintenance_started_at");
builder.Property(e => e.MaintenanceUntil).HasColumnName("maintenance_until");
builder.Property(e => e.MaintenanceStartedBy).HasColumnName("maintenance_started_by")
    .HasColumnType("nvarchar(100)").HasMaxLength(100);
builder.Property(e => e.DecommissionReason).HasColumnName("decommission_reason")
    .HasColumnType("nvarchar(512)").HasMaxLength(512);
builder.Property(e => e.DecommissionStartedAt).HasColumnName("decommission_started_at");
builder.Property(e => e.DecommissionGraceUntil).HasColumnName("decommission_grace_until");
builder.Property(e => e.DecommissionInitialOpenBatches).HasColumnName("decommission_initial_open_batches");
builder.Property(e => e.RowVersion).HasColumnName("row_version").IsRowVersion();
builder.HasIndex(e => e.LifecycleState).HasDatabaseName("IX_sync_node_status");
```

New configuration files:

```csharp
// src/MSOSync.Persistence/Configurations/SyncNodeLifecycleHistoryConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeLifecycleHistoryConfiguration : IEntityTypeConfiguration<SyncNodeLifecycleHistory>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeLifecycleHistory> builder)
    {
        builder.ToTable("sync_node_lifecycle_history", Schema);
        builder.HasKey(e => e.HistoryId);
        builder.Property(e => e.HistoryId).HasColumnName("history_id").UseIdentityColumn();
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.FromState).HasColumnName("from_state")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>();
        builder.Property(e => e.ToState).HasColumnName("to_state")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.Trigger).HasColumnName("trigger")
            .HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason")
            .HasColumnType("nvarchar(512)").HasMaxLength(512);
        builder.Property(e => e.Actor).HasColumnName("actor")
            .HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        builder.Property(e => e.MetadataJson).HasColumnName("metadata_json").HasColumnType("nvarchar(max)");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.HasOne<SyncNode>().WithMany().HasForeignKey(e => e.NodeId)
            .HasConstraintName("FK_node_lifecycle_history_node").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => new { e.NodeId, e.OccurredAt })
            .IsDescending(false, true).HasDatabaseName("IX_node_lifecycle_history_node_time");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncNodeConnectivityHistoryConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeConnectivityHistoryConfiguration : IEntityTypeConfiguration<SyncNodeConnectivityHistory>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeConnectivityHistory> builder)
    {
        builder.ToTable("sync_node_connectivity_history", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.PreviousStatus).HasColumnName("previous_status")
            .HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.NewStatus).HasColumnName("new_status")
            .HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.HasOne<SyncNode>().WithMany().HasForeignKey(e => e.NodeId)
            .HasConstraintName("FK_node_connectivity_history_node").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => new { e.NodeId, e.OccurredAt })
            .IsDescending(false, true).HasDatabaseName("IX_node_connectivity_history_node_time");
        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("IX_node_connectivity_history_time");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncNodeBootstrapTokenConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeBootstrapTokenConfiguration : IEntityTypeConfiguration<SyncNodeBootstrapToken>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeBootstrapToken> builder)
    {
        builder.ToTable("sync_node_bootstrap_token", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.TokenHash).HasColumnName("token_hash")
            .HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.IssuedBy).HasColumnName("issued_by")
            .HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.HasOne<SyncNode>().WithMany().HasForeignKey(e => e.NodeId)
            .HasConstraintName("FK_node_bootstrap_token_node").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => e.NodeId).HasDatabaseName("IX_node_bootstrap_token_node");
    }
}
```

In `src/MSOSync.Persistence/AppDbContext.cs`, add three DbSets next to `Nodes`:

```csharp
public DbSet<SyncNodeLifecycleHistory> NodeLifecycleHistories => Set<SyncNodeLifecycleHistory>();
public DbSet<SyncNodeConnectivityHistory> NodeConnectivityHistories => Set<SyncNodeConnectivityHistory>();
public DbSet<SyncNodeBootstrapToken> NodeBootstrapTokens => Set<SyncNodeBootstrapToken>();
```

(Match the property style already used in the file — if existing DbSets are `{ get; set; }` auto-properties, use that style instead.)

- [ ] **Step 5: Create policy services + permissions + startup validator**

```csharp
// src/MSOSync.Metadata/Lifecycle/INodeSyncPolicy.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public enum SyncEligibility
{
    Allowed,
    BlockedByLifecycle,
    BlockedByMaintenance,
    BlockedByDecommission,
    BlockedByPolicy,
}

public interface INodeSyncPolicy
{
    bool CanSynchronize(SyncNode node);
    SyncEligibility Evaluate(SyncNode node);
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/NodeSyncPolicy.cs
using System.Linq.Expressions;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed class NodeSyncPolicy : INodeSyncPolicy
{
    /// EF-translatable single source of eligibility for use inside IQueryable.Where.
    public static readonly Expression<Func<SyncNode, bool>> EligibleExpression =
        n => n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode;

    private static readonly Func<SyncNode, bool> Eligible = EligibleExpression.Compile();

    public bool CanSynchronize(SyncNode node) => Eligible(node);

    public SyncEligibility Evaluate(SyncNode node) => node.LifecycleState switch
    {
        NodeLifecycleState.Decommissioning or NodeLifecycleState.Decommissioned
            => SyncEligibility.BlockedByDecommission,
        not NodeLifecycleState.Active => SyncEligibility.BlockedByLifecycle,
        _ when node.MaintenanceMode => SyncEligibility.BlockedByMaintenance,
        _ => SyncEligibility.Allowed,
    };
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/IConnectivityPolicy.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed record ConnectivityTelemetry(
    NodeLifecycleState Lifecycle,
    DateTime? LastHeartbeatUtc,
    DateTime? LastProbeUtc,
    bool LastProbeFailed,
    int ConsecutiveProbeFailures,
    DateTime NowUtc,
    TimeSpan HeartbeatInterval,
    TimeSpan ProbeInterval);

public sealed record ConnectivityEvaluationResult(ConnectivityStatus Status, ConnectivityReason Reason);

public interface IConnectivityPolicy
{
    ConnectivityEvaluationResult Evaluate(ConnectivityTelemetry snapshot);
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/ConnectivityPolicy.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// Deterministic ordered rules — spec §5.2. Pure: no I/O, no clock (Now provided in snapshot).
public sealed class ConnectivityPolicy : IConnectivityPolicy
{
    public ConnectivityEvaluationResult Evaluate(ConnectivityTelemetry s)
    {
        // Rule 1 — excluded lifecycles
        if (s.Lifecycle is NodeLifecycleState.PendingApproval
                        or NodeLifecycleState.PendingRegistration
                        or NodeLifecycleState.Rejected
                        or NodeLifecycleState.Decommissioned)
            return new(ConnectivityStatus.Unknown, ConnectivityReason.NotEvaluated);

        // Rule 2 — no heartbeat ever received
        if (s.LastHeartbeatUtc is null)
            return new(ConnectivityStatus.Unknown, ConnectivityReason.NoHeartbeat);

        var heartbeatAge = s.NowUtc - s.LastHeartbeatUtc.Value;

        // Rule 3 — heartbeat expired
        if (heartbeatAge > 3 * s.HeartbeatInterval)
            return new(ConnectivityStatus.Unreachable, ConnectivityReason.HeartbeatExpired);

        // Rule 4 — heartbeat stale
        if (heartbeatAge > s.HeartbeatInterval)
            return new(ConnectivityStatus.Degraded, ConnectivityReason.HeartbeatStale);

        // Stale probes are ignored (spec §5.2): a just-rebooted healthy node is not
        // downgraded by a pre-reboot probe failure.
        var probeFresh = s.LastProbeUtc is not null
            && (s.NowUtc - s.LastProbeUtc.Value) <= 2 * s.ProbeInterval;

        // Rule 6 — 3+ consecutive fresh probe failures (checked before rule 5: stronger signal)
        if (probeFresh && s.LastProbeFailed && s.ConsecutiveProbeFailures >= 3)
            return new(ConnectivityStatus.Unreachable, ConnectivityReason.ProbeFailures);

        // Rule 5 — fresh probe failure
        if (probeFresh && s.LastProbeFailed)
            return new(ConnectivityStatus.Degraded, ConnectivityReason.ProbeFailed);

        // Rule 7
        return new(ConnectivityStatus.Reachable, ConnectivityReason.Healthy);
    }
}
```

In `src/MSOSync.Metadata/Permissions/SystemPermissions.cs`, add constants and extend `Defaults`:

```csharp
public const string ProvisionNodes      = "PROVISION_NODES";
public const string ManageNodeLifecycle = "MANAGE_NODE_LIFECYCLE";
```

- `OPERATOR` list: append `"MANAGE_NODE_LIFECYCLE"`.
- `ADMIN` list: append `"PROVISION_NODES", "MANAGE_NODE_LIFECYCLE"`.
- `VIEWER` unchanged.

```csharp
// src/MSOSync.Metadata/Lifecycle/LifecycleStartupValidator.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// Fail-fast startup check (spec §3.4): every status value must parse to NodeLifecycleState;
/// soft inconsistencies are logged as errors but do not block startup.
public sealed class LifecycleStartupValidator(
    IServiceScopeFactory scopeFactory,
    ILogger<LifecycleStartupValidator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Read raw status strings — do NOT materialize entities (enum conversion would
        // throw an opaque error; we want a precise diagnostic).
        var statuses = await db.Database
            .SqlQuery<string>($"SELECT status AS [Value] FROM [msosync].[sync_node]")
            .ToListAsync(ct);

        var invalid = statuses
            .Where(s => !Enum.TryParse<NodeLifecycleState>(s, ignoreCase: false, out _))
            .Distinct()
            .ToList();

        if (invalid.Count > 0)
            throw new InvalidOperationException(
                $"Lifecycle startup validation failed: unparseable sync_node.status values: {string.Join(", ", invalid)}. " +
                "Run migration M022 or repair the data before starting.");

        var inconsistent = await db.Nodes.AsNoTracking()
            .Where(n => n.MaintenanceMode &&
                (n.LifecycleState == NodeLifecycleState.Decommissioned
                 || n.LifecycleState == NodeLifecycleState.Rejected))
            .Select(n => n.NodeId)
            .ToListAsync(ct);

        foreach (var nodeId in inconsistent)
            logger.LogError(
                "Lifecycle consistency: node {NodeId} is terminal but has MaintenanceMode=true", nodeId);

        logger.LogInformation("Lifecycle startup validation passed ({Count} nodes)", statuses.Count);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Note: if `MSOSYNC_SCHEMA` env override is used elsewhere, build the SQL with the same
`Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync"` value via `SqlQueryRaw` instead of the interpolated form (schema names cannot be parameterized).

In `src/MSOSync.Metadata/MetadataServiceExtensions.cs`, inside `AddMetadata`, remove:

```csharp
services.AddScoped<INodeStateMachine, NodeStateMachine>();
```

and add:

```csharp
// Epic 12B-1 — Lifecycle policies
services.AddSingleton<INodeSyncPolicy, NodeSyncPolicy>();
services.AddSingleton<IConnectivityPolicy, ConnectivityPolicy>();
services.AddHostedService<LifecycleStartupValidator>();
```

(Remove the `using MSOSync.Metadata.Nodes;` import only if nothing else in the file needs it — `NodeMetadataService` may live in a different namespace; verify before removing.)

- [ ] **Step 6: Mechanical reader cutover (exact edits)**

Each bullet is old → new. Add `using MSOSync.Persistence.Entities;` and/or `using MSOSync.Metadata.Lifecycle;` where the new symbols require it.

1. **Delete** `src/MSOSync.Metadata/Nodes/INodeStateMachine.cs`, `src/MSOSync.Metadata/Nodes/NodeStateMachine.cs`, `src/MSOSync.Scheduler/Workers/NodeStatusWorker.cs` (`git rm` each by name).
2. `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` — remove `services.AddHostedService<NodeStatusWorker>();`.
3. `src/MSOSync.Api/Controllers/NodesController.cs` (Heartbeat action):
   - Remove the `INodeStateMachine` constructor/injected parameter and its using.
   - `if (node.Status == "DISABLED") return Forbid();` → `if (node.LifecycleState == NodeLifecycleState.Disabled) return Forbid();`
   - Delete the self-heal block:
     ```csharp
     // Self-heal: OFFLINE → REGISTERED
     if (node.Status == "OFFLINE")
         await stateMachine.TransitionAsync(nodeId, "REGISTERED", ct);
     ```
     (Task 3 installs the full lifecycle accept/reject matrix; this task only removes the lifecycle write.)
   - NOTE: `node` here is a DTO from `nodeService.GetNodeAsync` — if that DTO exposes `Status` as string, follow edit 9 below first; the comparison becomes whatever the updated DTO exposes (`LifecycleState`).
4. `src/MSOSync.Routing/RoutingService.cs:33` —
   `db.Nodes.Where(n => n.SyncEnabled)` → `db.Nodes.Where(NodeSyncPolicy.EligibleExpression)`.
5. `src/MSOSync.Scheduler/Workers/ProbeWorker.cs:61` —
   `.Where(n => n.UpstreamNodeId == localNodeId && n.SyncEnabled)` →
   `.Where(n => n.UpstreamNodeId == localNodeId && n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode)`
   (Task 3 rewrites this worker fully; keep this minimal.)
6. `src/MSOSync.Transport/SmartTransportService.cs:34` —
   `if (!node.SyncEnabled)` → `if (!(node.LifecycleState == NodeLifecycleState.Active && !node.MaintenanceMode))`
   (direct expression, no DI change needed; Task 2 does not revisit this — it is the eligibility rule inline where injecting the policy service would require constructor churn in Transport. If `SmartTransportService` already receives DI services and adding `INodeSyncPolicy` is a one-line constructor addition, prefer injecting and calling `syncPolicy.CanSynchronize(node)`.)
7. `src/MSOSync.Topology/TopologyService.cs:13` —
   `.Where(n => n.NodeId != localNodeId && n.Status == "APPROVED" && n.SyncEnabled)` →
   `.Where(n => n.NodeId != localNodeId && n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode)`
   (Legacy semantics: "APPROVED + enabled" meant sync-eligible; canonical equivalent is Active + not-in-maintenance.)
8. `src/MSOSync.Metadata/Topology/TopologyQueryService.cs:215,230` — replace `n.SyncEnabled` in both projections with `(n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode)`; rename the target DTO field per edit 9.
9. `src/MSOSync.Metadata/Topology/TopologyGroupDto.cs:21` and `src/MSOSync.Metadata/Dtos/NodeDto.cs:13` — rename record component `bool SyncEnabled` → `bool CanSynchronize`. Update every construction/usage site the compiler flags (mapping sites in `NodeMetadataService.MapNode`, `TopologyQueryService`). In `NodeDto` also rename `string Status` → `NodeLifecycleState LifecycleState` if present, and add `ConnectivityStatus ConnectivityStatus` if not already projected — check the DTO body; the goal is: no `Status` string and no `SyncEnabled` bool anywhere in DTOs after this task.
10. `src/MSOSync.Metadata/Services/NodeMetadataService.cs`:
    - Line ~91/102 (`EnableNodeAsync` / `DisableNodeAsync`): `node.SyncEnabled = true;` → `node.LifecycleState = NodeLifecycleState.Active;` and `node.SyncEnabled = false;` → `node.LifecycleState = NodeLifecycleState.Disabled;`
      Add comment on both: `// TEMPORARY direct write — replaced by NodeLifecycleService gateway in Task 2.`
    - Line ~131 (`ApproveRegistrationAsync`): `Status = "APPROVED",` → `LifecycleState = NodeLifecycleState.PendingRegistration,` (entire method deleted in Task 2).
    - Line ~188 (`CreateNodeAsync`): `Status = "PENDING",` → `LifecycleState = NodeLifecycleState.PendingRegistration,` (spec §4.4: admin creating IS the approval).
11. `src/MSOSync.Metadata/NodeManagement/NodeManagementService.cs:118-120` (overview counts):
    ```csharp
    nodes.Count(n => n.Status == "REGISTERED")  → nodes.Count(n => n.LifecycleState == NodeLifecycleState.Active)
    nodes.Count(n => n.Status == "OFFLINE")     → nodes.Count(n => n.ConnectivityStatus == ConnectivityStatus.Unreachable)
    nodes.Count(n => n.Status == "DEGRADED")    → nodes.Count(n => n.ConnectivityStatus == ConnectivityStatus.Degraded)
    ```
12. `src/MSOSync.Metadata/NodeManagement/NodeLifecycleService.cs`:
    - Line ~38: `existingNode.Status == "REGISTERED"` → `existingNode.LifecycleState == NodeLifecycleState.Active`
    - Line ~263: `Status = "PROVISIONED",` → `LifecycleState = NodeLifecycleState.PendingRegistration,`
13. `src/MSOSync.Persistence/Queries/GetOfflineNodesQuery.cs:13`:
    `n.Status == "REGISTERED"` → `n.LifecycleState == NodeLifecycleState.Active`
    (keep the heartbeat-staleness condition unchanged — semantics: "should be reachable but heartbeat stale").
14. `src/MSOSync.App/SignalR/NodeOperationsPublisher.cs` — NO change: the `"APPROVED"`/`"DISABLED"` literals there are `NodeMetadataChangedEvent.Action` values, not node status.
15. Build the solution; fix every remaining `Status`/`SyncEnabled` compile error using the same mappings (compiler is the checklist — `TreatWarningsAsErrors` catches unused usings too).

- [ ] **Step 7: Generate migration M022 and replace its body**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet ef migrations add M022_NodeLifecycle --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```

Then open the generated `<timestamp>_M022_NodeLifecycle.cs` and make the `Up` method perform, **in this order** (keep the scaffolded operations where they match; insert the `Sql` calls at the exact positions shown):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // 1. Widen status column BEFORE converting values
    migrationBuilder.AlterColumn<string>(
        name: "status", schema: "msosync", table: "sync_node",
        type: "varchar(30)", unicode: false, maxLength: 30, nullable: false,
        oldClrType: typeof(string), oldType: "varchar(20)", oldUnicode: false, oldMaxLength: 20);

    // 2. Convert legacy values (source of truth: LegacyStatusMap — keep in sync)
    migrationBuilder.Sql("""
        UPDATE [msosync].[sync_node] SET status = 'PendingApproval'     WHERE status = 'PENDING';
        UPDATE [msosync].[sync_node] SET status = 'PendingRegistration' WHERE status IN ('APPROVED','PROVISIONED');
        UPDATE [msosync].[sync_node] SET status = 'Active'              WHERE status IN ('REGISTERED','OFFLINE');
        UPDATE [msosync].[sync_node] SET status = 'Disabled'            WHERE status = 'DISABLED';
        """);

    // 3. Drop sync_enabled (scaffolded DropColumn stays)
    migrationBuilder.DropColumn(name: "sync_enabled", schema: "msosync", table: "sync_node");

    // 4. Scaffolded AddColumn operations for: previous_lifecycle_state, maintenance_mode,
    //    maintenance_reason, maintenance_started_at, maintenance_until, maintenance_started_by,
    //    decommission_reason, decommission_started_at, decommission_grace_until,
    //    decommission_initial_open_batches, connectivity_reason, last_probe_error,
    //    consecutive_probe_failures, row_version — keep exactly as scaffolded.

    // 5. Scaffolded CreateTable operations for sync_node_lifecycle_history,
    //    sync_node_connectivity_history, sync_node_bootstrap_token + their indexes — keep.

    // 6. Seed lifecycle history: one row per node, FromState = NULL, Trigger = Migration
    migrationBuilder.Sql("""
        INSERT INTO [msosync].[sync_node_lifecycle_history]
            (node_id, from_state, to_state, [trigger], reason, actor, occurred_at)
        SELECT node_id, NULL, status, 'Migration', 'M022 lifecycle model migration', 'system', SYSDATETIMEOFFSET()
        FROM [msosync].[sync_node];
        """);

    // 7. Permission seed (M018 pattern)
    migrationBuilder.InsertData(
        schema: "msosync",
        table: "sync_permission",
        columns: ["PermissionKey", "DisplayName", "Description", "Category", "SortOrder", "IsSystem"],
        values: new object[,]
        {
            { "PROVISION_NODES",       "Provision Nodes",       "Generate provisioning packages and bootstrap tokens",                    "OPERATIONS", 40, true },
            { "MANAGE_NODE_LIFECYCLE", "Manage Node Lifecycle", "Enable, disable, maintenance, decommission, and force-complete nodes",  "OPERATIONS", 50, true },
        });

    migrationBuilder.InsertData(
        schema: "msosync",
        table: "sync_role_permission",
        columns: ["RoleName", "PermissionKey"],
        values: new object[,]
        {
            { "OPERATOR", "MANAGE_NODE_LIFECYCLE" },
            { "ADMIN",    "PROVISION_NODES" },
            { "ADMIN",    "MANAGE_NODE_LIFECYCLE" },
        });
}
```

`Down`: reverse — `DeleteData` for the 3 role rows + 2 permissions, drop the 3 tables, drop the 14 added columns, re-add `sync_enabled` (bit, default true), reverse-convert:

```csharp
migrationBuilder.Sql("""
    UPDATE [msosync].[sync_node] SET status = 'PENDING'     WHERE status = 'PendingApproval';
    UPDATE [msosync].[sync_node] SET status = 'PROVISIONED' WHERE status = 'PendingRegistration';
    UPDATE [msosync].[sync_node] SET status = 'REGISTERED'  WHERE status IN ('Active','Recovery','Decommissioning','Decommissioned');
    UPDATE [msosync].[sync_node] SET status = 'DISABLED'    WHERE status IN ('Disabled','Rejected');
    """);
```

then narrow the column back to `varchar(20)`. (Down is lossy by design — hard cutover; document with a comment.)

- [ ] **Step 8: Fix existing tests referencing legacy model**

```pwsh
# Locate every legacy literal in test code:
# (use the Grep tool / IDE search, not shell grep on Windows)
#   "REGISTERED"  "PROVISIONED"  "APPROVED"  "PENDING"  "OFFLINE"  "DISABLED"  SyncEnabled  Status =
```

Apply the same mappings as Step 6. Known sites:
- `tests/MSOSync.IntegrationTests/NodeManagement/NodeManagementFixture.cs` — seeded node `Status = "REGISTERED"` → `LifecycleState = NodeLifecycleState.Active`.
- `tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs` — any `Status`/`SyncEnabled` assertions → enum equivalents (`CanSynchronize` where DTO renamed).
- `tests/MSOSync.MetadataTests/TestDbContext.cs` — extend the existing SQLite rowversion compatibility fix (from 12A `SyncRegistrationRequest.RowVersion`) to also cover `SyncNode.RowVersion`: same pattern, map `RowVersion` to a BLOB with default value for SQLite.
- Any test constructing `SyncNode` — property `Status` no longer exists; compiler flags every site.

- [ ] **Step 9: Run unit tests + full build**

```pwsh
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~MSOSync.MetadataTests.Lifecycle" -c Debug
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.MetadataTests -c Debug --no-build
```

Expected: new Lifecycle tests PASS; build zero warnings; full MetadataTests green.
(Pre-existing `MSOSync.TransportTests` CS7036 failures from Epic 6 are excluded per project convention — do not fix, do not worsen.)

- [ ] **Step 10: Commit**

```pwsh
git add src/MSOSync.Persistence/Entities/NodeLifecycleState.cs src/MSOSync.Persistence/Entities/LifecycleTrigger.cs src/MSOSync.Persistence/Entities/ConnectivityReason.cs src/MSOSync.Persistence/Entities/SyncNodeLifecycleHistory.cs src/MSOSync.Persistence/Entities/SyncNodeConnectivityHistory.cs src/MSOSync.Persistence/Entities/SyncNodeBootstrapToken.cs src/MSOSync.Persistence/LegacyStatusMap.cs src/MSOSync.Persistence/Entities/SyncNode.cs
git add src/MSOSync.Persistence/Configurations src/MSOSync.Persistence/AppDbContext.cs src/MSOSync.Persistence/Migrations
git add src/MSOSync.Metadata/Lifecycle src/MSOSync.Metadata/Permissions/SystemPermissions.cs src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.Routing/RoutingService.cs src/MSOSync.Scheduler/Workers/ProbeWorker.cs src/MSOSync.Scheduler/SyncSchedulerExtensions.cs src/MSOSync.Transport/SmartTransportService.cs src/MSOSync.Topology/TopologyService.cs src/MSOSync.Metadata/Topology src/MSOSync.Metadata/Dtos/NodeDto.cs src/MSOSync.Metadata/Services/NodeMetadataService.cs src/MSOSync.Metadata/NodeManagement src/MSOSync.Persistence/Queries/GetOfflineNodesQuery.cs src/MSOSync.Api/Controllers/NodesController.cs
git rm src/MSOSync.Metadata/Nodes/INodeStateMachine.cs src/MSOSync.Metadata/Nodes/NodeStateMachine.cs src/MSOSync.Scheduler/Workers/NodeStatusWorker.cs
git add tests/MSOSync.MetadataTests tests/MSOSync.IntegrationTests
git commit -m "feat(12B-1): canonical NodeLifecycleState model, M022 migration, sync/connectivity policies, legacy state machine removed"
```

(If any additional files were touched during compile-error cleanup, add them by name.)
