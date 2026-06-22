# Task 10: ITopologyService + TopologyService + PullJob

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 7
**Depends on:** Tasks 1, 3, 4, 5, 7, 8

**Files:**
- Create: `src/MSOSync.Topology/ITopologyService.cs`
- Create: `src/MSOSync.Topology/SourceNodeInfo.cs`
- Create: `src/MSOSync.Topology/TopologyService.cs`
- Create: `src/MSOSync.Topology/TopologyServiceExtensions.cs`
- Create: `src/MSOSync.Scheduler/PullJob.cs`
- Modify: `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj`

**Architectural note:** `MSOSync.Topology` project already exists (placeholder). `ITopologyService` and its implementation go there. Returns `IReadOnlyList<SourceNodeInfo>` (simple record with `NodeId` + `SyncUrl`) — avoids adding a Metadata → Topology dependency chain. `PullJob` is in `MSOSync.Scheduler`.

---

- [ ] **Step 1: Create SourceNodeInfo + ITopologyService**

Create `src/MSOSync.Topology/SourceNodeInfo.cs`:
```csharp
namespace MSOSync.Topology;

public sealed record SourceNodeInfo(string NodeId, string SyncUrl);
```

Create `src/MSOSync.Topology/ITopologyService.cs`:
```csharp
namespace MSOSync.Topology;

public interface ITopologyService
{
    /// <summary>
    /// Returns all Active sync nodes that are not the local node.
    /// CE assumption: flat topology, every non-self Active node is a source.
    /// </summary>
    Task<IReadOnlyList<SourceNodeInfo>> GetSourceNodesAsync(
        string localNodeId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create TopologyService**

Create `src/MSOSync.Topology/TopologyService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Topology;

public sealed class TopologyService(AppDbContext db) : ITopologyService
{
    public async Task<IReadOnlyList<SourceNodeInfo>> GetSourceNodesAsync(
        string localNodeId, CancellationToken ct = default)
    {
        var nodes = await db.Nodes
            .AsNoTracking()
            .Where(n => n.NodeId != localNodeId && n.Status == "APPROVED" && n.SyncEnabled)
            .OrderBy(n => n.NodeId)
            .Select(n => new SourceNodeInfo(n.NodeId, n.SyncUrl))
            .ToListAsync(ct);

        return nodes.AsReadOnly();
    }
}
```

- [ ] **Step 3: Create TopologyServiceExtensions**

Create `src/MSOSync.Topology/TopologyServiceExtensions.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Topology;

public static class TopologyServiceExtensions
{
    public static IServiceCollection AddTopologyServices(this IServiceCollection services)
    {
        services.AddScoped<ITopologyService, TopologyService>();
        return services;
    }
}
```

- [ ] **Step 4: Delete Placeholder.cs from Topology**

Delete `src/MSOSync.Topology/Placeholder.cs`.

- [ ] **Step 5: Update Scheduler.csproj**

Add Transport and Topology references to `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>SyncWorker, RetryWorker, HeartbeatWorker, PurgeWorker, MetricsWorker, NotificationWorker</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\MSOSync.Engine\MSOSync.Engine.csproj" />
    <ProjectReference Include="..\MSOSync.Transport\MSOSync.Transport.csproj" />
    <ProjectReference Include="..\MSOSync.Topology\MSOSync.Topology.csproj" />
    <ProjectReference Include="..\MSOSync.Metadata\MSOSync.Metadata.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create PullJob**

Create `src/MSOSync.Scheduler/PullJob.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;

namespace MSOSync.Scheduler;

public sealed class PullJob(
    IServiceScopeFactory     scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IConfiguration           config,
    ILogger<PullJob>         logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;

        // Self-check: if this node is in PUSH mode, PullJob is disabled
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var nodeMeta = scope.ServiceProvider.GetRequiredService<INodeMetadataService>();
            var ownNode  = await nodeMeta.GetNodeAsync(props.NodeId, ct);
            if (ownNode?.TransportMode == TransportMode.Push)
            {
                logger.LogInformation("PullJob disabled — node {NodeId} is in Push mode", props.NodeId);
                return;
            }
        }

        var intervalSeconds = config.GetValue<int>("Sync:PullIntervalSeconds", 10);
        var interval        = TimeSpan.FromSeconds(intervalSeconds);
        using var timer     = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await RunTickAsync(props.NodeId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "PullJob tick failed");
            }
        }
    }

    private async Task RunTickAsync(string localNodeId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var channelMeta  = sp.GetRequiredService<IChannelMetadataService>();
        var topology     = sp.GetRequiredService<ITopologyService>();
        var batchQuery   = sp.GetRequiredService<IBatchTransportQueryService>();
        var pullClient   = sp.GetRequiredService<PullClient>();
        var applyService = sp.GetRequiredService<IApplyService>();
        var clock        = sp.GetRequiredService<IClock>();
        var props        = nodeProps.Value;

        var channels = (await channelMeta.GetChannelsAsync(ct))
            .Where(c => c.Enabled)
            .OrderByDescending(c => c.Priority)
            .ToList();

        var sources = await topology.GetSourceNodesAsync(localNodeId, ct);

        foreach (var channel in channels)
        {
            foreach (var source in sources)
            {
                await PollSourceAsync(
                    source, channel.ChannelId, localNodeId, props,
                    batchQuery, pullClient, applyService, clock, ct);
            }
        }
    }

    private async Task PollSourceAsync(
        SourceNodeInfo           source,
        string                   channelId,
        string                   localNodeId,
        NodeProperties           props,
        IBatchTransportQueryService batchQuery,
        PullClient               pullClient,
        IApplyService            applyService,
        IClock                   clock,
        CancellationToken        ct)
    {
        var lastSeq = await batchQuery.GetLastSequenceAsync(source.NodeId, channelId, ct);

        while (true)
        {
            var request  = new PullRequest(localNodeId, channelId, lastSeq);
            var response = await pullClient.PullAsync(source.SyncUrl, request, ct);

            if (response == null)
            {
                // 204 No Content — nothing to pull
                logger.LogDebug("PullJob: no batches from {Source} channel {Ch}", source.NodeId, channelId);
                break;
            }

            foreach (var batch in response.Batches)
            {
                var applied = await ProcessBatchAsync(
                    batch, source, localNodeId, props, batchQuery, pullClient, applyService, clock, ct);
                if (applied)
                    lastSeq = batch.BatchSequence;
            }

            if (!response.MoreAvailable) break;
        }
    }

    private async Task<bool> ProcessBatchAsync(
        BatchPayload               batch,
        SourceNodeInfo             source,
        string                     localNodeId,
        NodeProperties             props,
        IBatchTransportQueryService batchQuery,
        PullClient                 pullClient,
        IApplyService              applyService,
        IClock                     clock,
        CancellationToken          ct)
    {
        var lastSeq = await batchQuery.GetLastSequenceAsync(source.NodeId, batch.ChannelId, ct);

        // Sequence gap check
        if (lastSeq + 1 != batch.BatchSequence)
        {
            logger.LogWarning(
                "PullJob: sequence gap from {Source} channel {Ch}: expected {Exp} got {Got}",
                source.NodeId, batch.ChannelId, lastSeq + 1, batch.BatchSequence);

            await pullClient.PostAckAsync(source.SyncUrl,
                new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                    false, "SEQUENCE_GAP", DateTimeOffset.UtcNow), ct);
            return false;
        }

        // Duplicate check
        if (await batchQuery.IncomingBatchExistsAsync(source.NodeId, batch.BatchSequence, ct))
        {
            logger.LogDebug("PullJob: duplicate batch source={Source} seq={Seq} — sending idempotent ACK",
                source.NodeId, batch.BatchSequence);
            await pullClient.PostAckAsync(source.SyncUrl,
                new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                    true, null, DateTimeOffset.UtcNow), ct);
            return true;
        }

        // Insert and apply
        var incoming = new SyncIncomingBatch
        {
            BatchId       = batch.BatchId,
            NodeId        = localNodeId,
            ChannelId     = batch.ChannelId,
            SourceNodeId  = source.NodeId,
            BatchSequence = batch.BatchSequence,
            ReceivedTime  = clock.UtcNow,
            RowCount      = batch.RowCount,
            Status        = IncomingBatchStatus.New
        };

        await batchQuery.InsertIncomingBatchAsync(incoming, ct);
        var result  = await applyService.ApplyAsync(incoming, batch, ct);
        var ackTime = DateTimeOffset.UtcNow;

        await pullClient.PostAckAsync(source.SyncUrl,
            new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                result.Success, result.ErrorMessage, ackTime), ct);

        return result.Success;
    }
}
```

- [ ] **Step 7: Build to verify**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: zero warnings, zero errors.

- [ ] **Step 8: Commit**

```pwsh
git add src/MSOSync.Topology/ITopologyService.cs
git add src/MSOSync.Topology/SourceNodeInfo.cs
git add src/MSOSync.Topology/TopologyService.cs
git add src/MSOSync.Topology/TopologyServiceExtensions.cs
git rm src/MSOSync.Topology/Placeholder.cs
git add src/MSOSync.Scheduler/PullJob.cs
git add src/MSOSync.Scheduler/MSOSync.Scheduler.csproj
git commit -m "feat(epic6): ITopologyService + TopologyService + PullJob with channel-priority ordering"
```
