# 2B.1 Task 3 — Heartbeat: allow Draining + persist AgentVersion

**Files:**
- Modify: `src/MSOSync.Api/Controllers/NodesController.cs` (heartbeat state matrix)
- Modify: `src/MSOSync.Metadata/Configuration/HeartbeatProcessor.cs`
- Test: `tests/MSOSync.MetadataTests/Configuration/HeartbeatProcessorTests.cs` (existing — add cases; if named differently, `grep -rln "HeartbeatProcessor" tests/`)

**Interfaces:**
- Consumes: Task 1 (`SyncNode.AgentVersion`), Task 2 (`Draining` reachable).
- Produces: heartbeat from `Draining` node → 200; `HeartbeatProcessor.ProcessAsync` persists `request.NodeVersion` into `SyncNode.AgentVersion`. Wire contract unchanged.

- [ ] **Step 1: Failing processor test**

Add to the HeartbeatProcessor test file (reuse its existing fixture/InMemory context setup):

```csharp
[Fact]
public async Task ProcessAsync_persists_node_version_as_agent_version()
{
    // arrange: seed node via existing fixture helper, then:
    var request = new HeartbeatRequest(NodeId: nodeId, NodeVersion: "2.4.1",
        UptimeSeconds: 10, DatabaseType: "SqlServer", TransportMode: "Push");

    await processor.ProcessAsync(nodeId, request, CancellationToken.None);

    var node = await db.Nodes.SingleAsync(n => n.NodeId == nodeId);
    node.AgentVersion.Should().Be("2.4.1");
}
```

Run: `dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~HeartbeatProcessor" --nologo`
Expected: FAIL (`AgentVersion` null).

- [ ] **Step 2: Persist in `HeartbeatProcessor.ProcessAsync`**

Where the processor already loads/updates the `SyncNode` (it queries `db` for drift bookkeeping — find where the tracked node is available or fetch it), add before `SaveChangesAsync`:

```csharp
if (!string.IsNullOrWhiteSpace(request.NodeVersion) && node.AgentVersion != request.NodeVersion)
    node.AgentVersion = request.NodeVersion;
```

If the processor currently reads the node `AsNoTracking`, switch that one query to tracked or issue a targeted update — prefer tracked read, keep single `SaveChangesAsync`.

Run same filter. Expected: PASS.

- [ ] **Step 3: Heartbeat state matrix**

In `NodesController.Heartbeat` switch, add `Draining` to the accepted group:

```csharp
case NodeLifecycleState.Active:
case NodeLifecycleState.Recovery:
case NodeLifecycleState.Decommissioning:
case NodeLifecycleState.Draining:
    break;
```

- [ ] **Step 4: Full metadata suite + commit**

```powershell
dotnet test tests/MSOSync.MetadataTests --nologo
```

Expected: green.

```powershell
git add src/MSOSync.Api/Controllers/NodesController.cs src/MSOSync.Metadata/Configuration/HeartbeatProcessor.cs tests/MSOSync.MetadataTests/
git commit -m "feat(2B.1-T3): heartbeat accepts Draining, persists AgentVersion"
```
