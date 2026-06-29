# Task 1: NodeStatus Enum + Topology DTOs

**Part of:** [Epic 9B Plan](2026-06-29-epic9b-topology-apis.md)

**Goal:** Create the `NodeStatus` enum in `MSOSync.Persistence` and the 3 DTO files in `MSOSync.Metadata/Topology/`. No tests needed — pure data-shape definitions. End with a clean build.

**Files:**
- Create: `src/MSOSync.Persistence/NodeStatus.cs`
- Create: `src/MSOSync.Metadata/Topology/TopologyGraphDto.cs`
- Create: `src/MSOSync.Metadata/Topology/TopologyGroupDto.cs`
- Create: `src/MSOSync.Metadata/Topology/TopologySummaryDto.cs`

**Interfaces:**
- Consumes: `MSOSync.Persistence.ConnectivityStatus` (already exists)
- Produces (used in Task 2 and 3):
  - `NodeStatus` enum: `Pending=0, Registered=1, Offline=2, Disabled=3`
  - `TopologyGraphDto(IReadOnlyList<TopologyNodeDto>, IReadOnlyList<TopologyEdgeDto>, TopologyMetadataDto)`
  - `TopologyNodeDto(string GroupId, string? Name, int TotalNodes, int ReachableNodes, int DegradedNodes, int UnreachableNodes, int UnknownNodes, ConnectivityStatus ConnectivityStatus)`
  - `TopologyEdgeDto(string RouterId, string SourceGroupId, string TargetGroupId, IReadOnlyList<string> ChannelIds, bool Enabled)`
  - `TopologyMetadataDto(string LayoutHint, string Direction, int Version, DateTime GeneratedAt)`
  - `TopologyGroupDto(string GroupId, string? Name, int TotalNodes, int ReachableNodes, int DegradedNodes, int UnreachableNodes, int UnknownNodes, ConnectivityStatus ConnectivityStatus)`
  - `TopologyGroupNodeDto(string NodeId, NodeStatus Status, ConnectivityStatus ConnectivityStatus, DateTime? LastHeartbeat, int? LastProbeLatencyMs, bool SyncEnabled)`
  - `TopologySummaryDto(int TotalGroups, int TotalNodes, int ReachableNodes, int DegradedNodes, int UnreachableNodes, int UnknownNodes, DateTime GeneratedAt)`

---

- [ ] **Step 1: Create NodeStatus.cs in MSOSync.Persistence**

The persisted strings (verified from `NodeStateMachine.cs`) are: `"PENDING"`, `"REGISTERED"`, `"OFFLINE"`, `"DISABLED"` — all uppercase. The enum names match exactly so `Enum.Parse<NodeStatus>(value, ignoreCase: true)` works.

Create `src/MSOSync.Persistence/NodeStatus.cs`:

```csharp
namespace MSOSync.Persistence;

public enum NodeStatus
{
    Pending    = 0,
    Registered = 1,
    Offline    = 2,
    Disabled   = 3
}
```

- [ ] **Step 2: Create TopologyGraphDto.cs**

Create `src/MSOSync.Metadata/Topology/TopologyGraphDto.cs`:

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed record TopologyGraphDto(
    IReadOnlyList<TopologyNodeDto> Nodes,
    IReadOnlyList<TopologyEdgeDto> Edges,
    TopologyMetadataDto            Metadata);

public sealed record TopologyNodeDto(
    string             GroupId,
    string?            Name,
    int                TotalNodes,
    int                ReachableNodes,
    int                DegradedNodes,
    int                UnreachableNodes,
    int                UnknownNodes,
    ConnectivityStatus ConnectivityStatus);

public sealed record TopologyEdgeDto(
    string                RouterId,
    string                SourceGroupId,
    string                TargetGroupId,
    IReadOnlyList<string> ChannelIds,
    bool                  Enabled);

public sealed record TopologyMetadataDto(
    string   LayoutHint,
    string   Direction,
    int      Version,
    DateTime GeneratedAt);
```

- [ ] **Step 3: Create TopologyGroupDto.cs**

Create `src/MSOSync.Metadata/Topology/TopologyGroupDto.cs`:

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed record TopologyGroupDto(
    string             GroupId,
    string?            Name,
    int                TotalNodes,
    int                ReachableNodes,
    int                DegradedNodes,
    int                UnreachableNodes,
    int                UnknownNodes,
    ConnectivityStatus ConnectivityStatus);

public sealed record TopologyGroupNodeDto(
    string             NodeId,
    NodeStatus         Status,
    ConnectivityStatus ConnectivityStatus,
    DateTime?          LastHeartbeat,
    int?               LastProbeLatencyMs,
    bool               SyncEnabled);
```

- [ ] **Step 4: Create TopologySummaryDto.cs**

Create `src/MSOSync.Metadata/Topology/TopologySummaryDto.cs`:

```csharp
namespace MSOSync.Metadata.Topology;

public sealed record TopologySummaryDto(
    int      TotalGroups,
    int      TotalNodes,
    int      ReachableNodes,
    int      DegradedNodes,
    int      UnreachableNodes,
    int      UnknownNodes,
    DateTime GeneratedAt);
```

- [ ] **Step 5: Build and verify**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Commit**

```powershell
git add src/MSOSync.Persistence/NodeStatus.cs
git add src/MSOSync.Metadata/Topology/TopologyGraphDto.cs
git add src/MSOSync.Metadata/Topology/TopologyGroupDto.cs
git add src/MSOSync.Metadata/Topology/TopologySummaryDto.cs
git commit -m "feat(9b): add NodeStatus enum and topology DTOs"
```
