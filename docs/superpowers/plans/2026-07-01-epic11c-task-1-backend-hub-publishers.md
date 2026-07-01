# Epic 11C Task 1: Backend Hub + Publishers + JWT + Tests

## Context

You are wiring up the SignalR backend for Epic 11C. The goal is to push `OperationsEvent` messages to all connected authenticated clients whenever: a node's connectivity status changes (ProbeWorker), a node lifecycle action is taken (approved/rejected/enabled/disabled via NodeMetadataService), or a sync cycle completes (SyncEngine).

**Key facts about the existing codebase:**
- `NodeMetadataChangedEvent(string NodeId, string Action)` exists in `MSOSync.Metadata/Events/` — `NodeMetadataService` already publishes this with actions `"APPROVED"`, `"REJECTED"`, `"ENABLED"`, `"DISABLED"`, `"UPDATED"`
- `SyncCycleCompletedEvent(int EventsRead, int BatchesCreated, TimeSpan Duration)` exists in `MSOSync.Engine/`
- `ProbeWorker` in `MSOSync.Scheduler/Workers/ProbeWorker.cs` updates `ConnectivityStatus` via `ExecuteUpdateAsync` but does NOT publish any MediatR event — this is the gap to fill
- `NodeStateMachine` handles automatic REGISTERED/OFFLINE lifecycle transitions — it does NOT need SignalR wiring (those are internal node-to-node transitions, not operator-visible events)
- `MSOSync.Scheduler` already has `<PackageReference Include="MediatR" />` and MediatR is available via DI in the app host
- `SecurityServiceExtensions.AddSecurity()` registers JWT bearer with no `Events` property currently
- `MSOSync.App.csproj` references `MSOSync.Scheduler` via `<ProjectReference>` — so App can reference types from Scheduler
- `MSOSync.App` is `Microsoft.NET.Sdk.Web` — `Microsoft.AspNetCore.SignalR` is already included (no NuGet install needed)
- There is NO `MSOSync.AppTests` test project yet — you must create it

## Interfaces

**Consumes:**
- `NodeMetadataChangedEvent(string NodeId, string Action)` from `MSOSync.Metadata.Events`
- `SyncCycleCompletedEvent(int EventsRead, int BatchesCreated, TimeSpan Duration)` from `MSOSync.Engine`
- `NodeConnectivityChangedEvent(string NodeId, ConnectivityStatus PreviousStatus, ConnectivityStatus NewStatus)` — NEW, created in this task in `MSOSync.Scheduler`
- `ConnectivityStatus` enum from `MSOSync.Persistence.Entities`

**Produces:**
- `OperationsHub` at `/hubs/operations` — consumed by frontend in Task 2
- `OperationsEvent` DTO — consumed by frontend in Task 2
- `OperationsEventType` enum — consumed by frontend in Task 2

---

## Files

- Create: `src/MSOSync.Scheduler/NodeConnectivityChangedEvent.cs`
- Modify: `src/MSOSync.Scheduler/Workers/ProbeWorker.cs`
- Create: `src/MSOSync.App/Hubs/OperationsHub.cs`
- Create: `src/MSOSync.App/SignalR/OperationsEventType.cs`
- Create: `src/MSOSync.App/SignalR/OperationsEvent.cs`
- Create: `src/MSOSync.App/SignalR/NodeOperationsPublisher.cs`
- Create: `src/MSOSync.App/SignalR/SyncOperationsPublisher.cs`
- Modify: `src/MSOSync.Security/SecurityServiceExtensions.cs`
- Modify: `src/MSOSync.App/Program.cs`
- Create: `tests/MSOSync.AppTests/MSOSync.AppTests.csproj`
- Create: `tests/MSOSync.AppTests/SignalR/NodeOperationsPublisherTests.cs`
- Create: `tests/MSOSync.AppTests/SignalR/SyncOperationsPublisherTests.cs`

---

- [ ] **Step 1: Create `NodeConnectivityChangedEvent`**

Create `src/MSOSync.Scheduler/NodeConnectivityChangedEvent.cs`:

```csharp
using MediatR;
using MSOSync.Persistence.Entities;

namespace MSOSync.Scheduler;

public sealed record NodeConnectivityChangedEvent(
    string NodeId,
    ConnectivityStatus PreviousStatus,
    ConnectivityStatus NewStatus) : INotification;
```

- [ ] **Step 2: Update `ProbeWorker` to publish connectivity changes**

Open `src/MSOSync.Scheduler/Workers/ProbeWorker.cs`. Add `IPublisher` parameter to the primary constructor and capture the previous status before `ExecuteUpdateAsync`. Publish `NodeConnectivityChangedEvent` when status changes.

Current constructor and `RunProbeTickAsync`:
```csharp
public sealed class ProbeWorker(
    IServiceScopeFactory     scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IConfiguration           config,
    ILogger<ProbeWorker>     logger) : BackgroundService
```

```csharp
private async Task RunProbeTickAsync(string localNodeId, CancellationToken ct)
{
    await using var scope = scopeFactory.CreateAsyncScope();
    var db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var httpClient = scope.ServiceProvider.GetRequiredService<INodeHttpClient>();

    var children = await db.Nodes.AsNoTracking()
        .Where(n => n.UpstreamNodeId == localNodeId && n.SyncEnabled)
        .ToListAsync(ct);

    foreach (var child in children)
    {
        var sw     = Stopwatch.StartNew();
        ConnectivityStatus status;

        try
        {
            await httpClient.PostAsync<object, object>(
                $"{child.SyncUrl}/api/v1/sync/ping", new { }, child.NodeId, string.Empty, ct);
            sw.Stop();

            status = sw.ElapsedMilliseconds switch
            {
                < 500  => ConnectivityStatus.Reachable,
                < 2000 => ConnectivityStatus.Degraded,
                _      => ConnectivityStatus.Unreachable
            };
            Success.Add(1);
        }
        catch
        {
            sw.Stop();
            status = ConnectivityStatus.Unreachable;
            Failure.Add(1);
        }

        await db.Nodes
            .Where(n => n.NodeId == child.NodeId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(n => n.ConnectivityStatus, status)
                 .SetProperty(n => n.LastProbeTime,      DateTime.UtcNow)
                 .SetProperty(n => n.LastProbeLatencyMs, (int)sw.ElapsedMilliseconds),
                ct);

        logger.LogDebug("ProbeWorker: {NodeId} → {Status} ({Ms}ms)",
            child.NodeId, status, sw.ElapsedMilliseconds);
    }
}
```

Replace the entire `ProbeWorker` class with this updated version (adds `IPublisher publisher` to constructor, captures `previousStatus`, publishes when changed):

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;
using MSOSync.Transport;

namespace MSOSync.Scheduler.Workers;

public sealed class ProbeWorker(
    IServiceScopeFactory     scopeFactory,
    IPublisher               publisher,
    IOptions<NodeProperties> nodeProps,
    IConfiguration           config,
    ILogger<ProbeWorker>     logger) : BackgroundService
{
    private static readonly Meter         Meter   = new("MSOSync.Probe", "1.0.0");
    private static readonly Counter<long> Success = Meter.CreateCounter<long>("msosync_probe_success_total");
    private static readonly Counter<long> Failure = Meter.CreateCounter<long>("msosync_probe_failure_total");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props    = nodeProps.Value;
        var interval = TimeSpan.FromSeconds(
            config.GetValue<int>("Heartbeat:ProbeIntervalSeconds", 60));

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var topology = scope.ServiceProvider.GetRequiredService<ITopologyService>();
            if (!await topology.IsHubAsync(props.NodeId, ct))
            {
                logger.LogInformation("ProbeWorker disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await RunProbeTickAsync(props.NodeId, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "ProbeWorker tick failed"); }
        }
    }

    private async Task RunProbeTickAsync(string localNodeId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var httpClient = scope.ServiceProvider.GetRequiredService<INodeHttpClient>();

        var children = await db.Nodes.AsNoTracking()
            .Where(n => n.UpstreamNodeId == localNodeId && n.SyncEnabled)
            .ToListAsync(ct);

        foreach (var child in children)
        {
            var previousStatus = child.ConnectivityStatus;
            var sw             = Stopwatch.StartNew();
            ConnectivityStatus status;

            try
            {
                await httpClient.PostAsync<object, object>(
                    $"{child.SyncUrl}/api/v1/sync/ping", new { }, child.NodeId, string.Empty, ct);
                sw.Stop();

                status = sw.ElapsedMilliseconds switch
                {
                    < 500  => ConnectivityStatus.Reachable,
                    < 2000 => ConnectivityStatus.Degraded,
                    _      => ConnectivityStatus.Unreachable
                };
                Success.Add(1);
            }
            catch
            {
                sw.Stop();
                status = ConnectivityStatus.Unreachable;
                Failure.Add(1);
            }

            await db.Nodes
                .Where(n => n.NodeId == child.NodeId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(n => n.ConnectivityStatus, status)
                     .SetProperty(n => n.LastProbeTime,      DateTime.UtcNow)
                     .SetProperty(n => n.LastProbeLatencyMs, (int)sw.ElapsedMilliseconds),
                    ct);

            logger.LogDebug("ProbeWorker: {NodeId} → {Status} ({Ms}ms)",
                child.NodeId, status, sw.ElapsedMilliseconds);

            if (status != previousStatus)
                await publisher.Publish(
                    new NodeConnectivityChangedEvent(child.NodeId, previousStatus, status), ct);
        }
    }
}
```

- [ ] **Step 3: Create `OperationsEventType`**

Create `src/MSOSync.App/SignalR/OperationsEventType.cs`:

```csharp
namespace MSOSync.App.SignalR;

public enum OperationsEventType
{
    NodeHealthChanged,
    NodeApproved,
    NodeRejected,
    NodeDisabled,
    NodeEnabled,
    SyncCycleCompleted
}
```

Note: `NodeRegistered` is omitted — there is no registration submission endpoint yet in the codebase. It will be added in a future epic when the node self-registration endpoint is implemented.

- [ ] **Step 4: Create `OperationsEvent`**

Create `src/MSOSync.App/SignalR/OperationsEvent.cs`:

```csharp
namespace MSOSync.App.SignalR;

public sealed record OperationsEvent(
    OperationsEventType Type,
    string NodeId,
    string? NodeLabel,
    string? PreviousStatus,
    string? CurrentStatus,
    DateTimeOffset OccurredAt,
    string? GroupId = null);
```

- [ ] **Step 5: Create `OperationsHub`**

Create `src/MSOSync.App/Hubs/OperationsHub.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MSOSync.App.Hubs;

[Authorize(Policy = "ViewerOrAbove")]
public sealed class OperationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "operators");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "operators");
        await base.OnDisconnectedAsync(exception);
    }
}
```

No client-invokable methods — pure server push.

- [ ] **Step 6: Create `NodeOperationsPublisher`**

Create `src/MSOSync.App/SignalR/NodeOperationsPublisher.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Events;
using MSOSync.Scheduler;

namespace MSOSync.App.SignalR;

public sealed class NodeOperationsPublisher(
    IHubContext<OperationsHub> hub)
    : INotificationHandler<NodeMetadataChangedEvent>,
      INotificationHandler<NodeConnectivityChangedEvent>
{
    public async Task Handle(NodeMetadataChangedEvent notification, CancellationToken ct)
    {
        var type = notification.Action switch
        {
            "APPROVED" => OperationsEventType.NodeApproved,
            "REJECTED" => OperationsEventType.NodeRejected,
            "DISABLED" => OperationsEventType.NodeDisabled,
            "ENABLED"  => OperationsEventType.NodeEnabled,
            _          => (OperationsEventType?)null
        };

        if (type is null) return;

        var evt = new OperationsEvent(
            Type:           type.Value,
            NodeId:         notification.NodeId,
            NodeLabel:      null,
            PreviousStatus: null,
            CurrentStatus:  null,
            OccurredAt:     DateTimeOffset.UtcNow);

        await hub.Clients.Group("operators")
            .SendAsync("OperationsEvent", evt, ct);
    }

    public async Task Handle(NodeConnectivityChangedEvent notification, CancellationToken ct)
    {
        var evt = new OperationsEvent(
            Type:           OperationsEventType.NodeHealthChanged,
            NodeId:         notification.NodeId,
            NodeLabel:      null,
            PreviousStatus: notification.PreviousStatus.ToString(),
            CurrentStatus:  notification.NewStatus.ToString(),
            OccurredAt:     DateTimeOffset.UtcNow);

        await hub.Clients.Group("operators")
            .SendAsync("OperationsEvent", evt, ct);
    }
}
```

- [ ] **Step 7: Create `SyncOperationsPublisher`**

Create `src/MSOSync.App/SignalR/SyncOperationsPublisher.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Engine;

namespace MSOSync.App.SignalR;

public sealed class SyncOperationsPublisher(
    IHubContext<OperationsHub> hub)
    : INotificationHandler<SyncCycleCompletedEvent>
{
    public async Task Handle(SyncCycleCompletedEvent notification, CancellationToken ct)
    {
        var evt = new OperationsEvent(
            Type:           OperationsEventType.SyncCycleCompleted,
            NodeId:         "system",
            NodeLabel:      null,
            PreviousStatus: null,
            CurrentStatus:  null,
            OccurredAt:     DateTimeOffset.UtcNow,
            GroupId:        "global");

        await hub.Clients.Group("operators")
            .SendAsync("OperationsEvent", evt, ct);
    }
}
```

- [ ] **Step 8: Add JWT `OnMessageReceived` to `SecurityServiceExtensions`**

Open `src/MSOSync.Security/SecurityServiceExtensions.cs`. Find the `.AddJwtBearer(options => { ... })` block. Add `options.Events` inside it:

Current:
```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer   = true,
            ValidIssuer      = jwtIssuer,
            ValidateAudience = true,
            ValidAudience    = jwtAudience,
            ClockSkew        = TimeSpan.FromSeconds(30)
        };
    });
```

Updated (add the `options.Events` block after `TokenValidationParameters`):
```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer   = true,
            ValidIssuer      = jwtIssuer,
            ValidateAudience = true,
            ValidAudience    = jwtAudience,
            ClockSkew        = TimeSpan.FromSeconds(30)
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path        = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
```

- [ ] **Step 9: Wire SignalR in `Program.cs`**

Open `src/MSOSync.App/Program.cs`.

**Step 9a:** Add `using System.Text.Json.Serialization;` at the top of the file if not already present.

**Step 9b:** After `builder.Services.AddTopologyServices();`, add:

```csharp
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters
            .Add(new JsonStringEnumConverter());
    });

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<MSOSync.App.SignalR.NodeOperationsPublisher>());
```

**Step 9c:** After `app.MapControllers();`, add:

```csharp
app.MapHub<MSOSync.App.Hubs.OperationsHub>("/hubs/operations");
```

- [ ] **Step 10: Create `MSOSync.AppTests.csproj`**

Create `tests/MSOSync.AppTests/MSOSync.AppTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.App\MSOSync.App.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Scheduler\MSOSync.Scheduler.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Engine\MSOSync.Engine.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
  </ItemGroup>
</Project>
```

Add the project to the solution:
```pwsh
cd D:\MSOSync
dotnet sln add tests/MSOSync.AppTests/MSOSync.AppTests.csproj
```

- [ ] **Step 11: Create `NodeOperationsPublisherTests`**

Create `tests/MSOSync.AppTests/SignalR/NodeOperationsPublisherTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using MSOSync.App.Hubs;
using MSOSync.App.SignalR;
using MSOSync.Metadata.Events;
using MSOSync.Persistence.Entities;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.AppTests.SignalR;

public sealed class NodeOperationsPublisherTests
{
    private readonly Mock<IHubContext<OperationsHub>>      _hubCtx    = new();
    private readonly Mock<IHubClients>                     _clients   = new();
    private readonly Mock<IClientProxy>                    _group     = new();
    private readonly List<(string Method, object? Arg)>   _sent      = [];
    private readonly NodeOperationsPublisher               _publisher;

    public NodeOperationsPublisherTests()
    {
        _hubCtx.Setup(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.Group("operators")).Returns(_group.Object);
        _group
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) =>
                _sent.Add((method, args.Length > 0 ? args[0] : null)))
            .Returns(Task.CompletedTask);

        _publisher = new NodeOperationsPublisher(_hubCtx.Object);
    }

    private OperationsEvent? LastSent => _sent.LastOrDefault().Arg as OperationsEvent;

    [Fact]
    public async Task Handle_Approved_SendsNodeApprovedEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-1", "APPROVED"), CancellationToken.None);

        _sent.Should().HaveCount(1);
        _sent[0].Method.Should().Be("OperationsEvent");
        LastSent!.Type.Should().Be(OperationsEventType.NodeApproved);
        LastSent.NodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task Handle_Rejected_SendsNodeRejectedEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-2", "REJECTED"), CancellationToken.None);

        LastSent!.Type.Should().Be(OperationsEventType.NodeRejected);
        LastSent.NodeId.Should().Be("node-2");
    }

    [Fact]
    public async Task Handle_Disabled_SendsNodeDisabledEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-3", "DISABLED"), CancellationToken.None);

        LastSent!.Type.Should().Be(OperationsEventType.NodeDisabled);
        LastSent.NodeId.Should().Be("node-3");
    }

    [Fact]
    public async Task Handle_Enabled_SendsNodeEnabledEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-4", "ENABLED"), CancellationToken.None);

        LastSent!.Type.Should().Be(OperationsEventType.NodeEnabled);
        LastSent.NodeId.Should().Be("node-4");
    }

    [Fact]
    public async Task Handle_Updated_SendsNothing()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-5", "UPDATED"), CancellationToken.None);

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ConnectivityChanged_SendsNodeHealthChangedEvent()
    {
        await _publisher.Handle(
            new NodeConnectivityChangedEvent(
                "node-6",
                ConnectivityStatus.Reachable,
                ConnectivityStatus.Degraded),
            CancellationToken.None);

        _sent.Should().HaveCount(1);
        _sent[0].Method.Should().Be("OperationsEvent");
        LastSent!.Type.Should().Be(OperationsEventType.NodeHealthChanged);
        LastSent.NodeId.Should().Be("node-6");
        LastSent.PreviousStatus.Should().Be("Reachable");
        LastSent.CurrentStatus.Should().Be("Degraded");
    }

    [Fact]
    public async Task Handle_ConnectivityChanged_UsesOperatorsGroup()
    {
        await _publisher.Handle(
            new NodeConnectivityChangedEvent(
                "node-7",
                ConnectivityStatus.Unknown,
                ConnectivityStatus.Reachable),
            CancellationToken.None);

        _clients.Verify(c => c.Group("operators"), Times.Once);
    }
}
```

- [ ] **Step 12: Create `SyncOperationsPublisherTests`**

Create `tests/MSOSync.AppTests/SignalR/SyncOperationsPublisherTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using MSOSync.App.Hubs;
using MSOSync.App.SignalR;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.AppTests.SignalR;

public sealed class SyncOperationsPublisherTests
{
    private readonly Mock<IHubContext<OperationsHub>>    _hubCtx  = new();
    private readonly Mock<IHubClients>                   _clients = new();
    private readonly Mock<IClientProxy>                  _group   = new();
    private readonly List<(string Method, object? Arg)> _sent    = [];
    private readonly SyncOperationsPublisher             _publisher;

    public SyncOperationsPublisherTests()
    {
        _hubCtx.Setup(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.Group("operators")).Returns(_group.Object);
        _group
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) =>
                _sent.Add((method, args.Length > 0 ? args[0] : null)))
            .Returns(Task.CompletedTask);

        _publisher = new SyncOperationsPublisher(_hubCtx.Object);
    }

    private OperationsEvent? LastSent => _sent.LastOrDefault().Arg as OperationsEvent;

    [Fact]
    public async Task Handle_SyncCycleCompleted_SendsSyncCycleCompletedEvent()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(100, 5, TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        _sent.Should().HaveCount(1);
        _sent[0].Method.Should().Be("OperationsEvent");
        LastSent!.Type.Should().Be(OperationsEventType.SyncCycleCompleted);
    }

    [Fact]
    public async Task Handle_SyncCycleCompleted_UsesSystemNodeId()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(0, 0, TimeSpan.Zero),
            CancellationToken.None);

        LastSent!.NodeId.Should().Be("system");
    }

    [Fact]
    public async Task Handle_SyncCycleCompleted_UsesGlobalGroupId()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(0, 0, TimeSpan.Zero),
            CancellationToken.None);

        LastSent!.GroupId.Should().Be("global");
    }

    [Fact]
    public async Task Handle_SyncCycleCompleted_UsesOperatorsGroup()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(50, 3, TimeSpan.FromSeconds(1)),
            CancellationToken.None);

        _clients.Verify(c => c.Group("operators"), Times.Once);
    }
}
```

- [ ] **Step 13: Run the unit tests**

```pwsh
cd D:\MSOSync
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.AppTests -c Debug
```

Expected: all tests pass (2 test classes, 11 tests).

If you see errors about missing package versions, check `Directory.Packages.props` in the repo root and use the versions listed there.

- [ ] **Step 14: Verify full build is clean**

```pwsh
cd D:\MSOSync
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: exit 0, no warnings or errors.

Common issues:
- If `JsonStringEnumConverter` is unresolved in Program.cs: add `using System.Text.Json.Serialization;`
- If `NodeConnectivityChangedEvent` is not found in `NodeOperationsPublisher`: verify the `using MSOSync.Scheduler;` directive
- If MediatR handler registration test fails: verify `AddMediatR` in Program.cs scans `NodeOperationsPublisher` assembly

- [ ] **Step 15: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Scheduler/NodeConnectivityChangedEvent.cs
git add src/MSOSync.Scheduler/Workers/ProbeWorker.cs
git add src/MSOSync.App/Hubs/OperationsHub.cs
git add src/MSOSync.App/SignalR/OperationsEventType.cs
git add src/MSOSync.App/SignalR/OperationsEvent.cs
git add src/MSOSync.App/SignalR/NodeOperationsPublisher.cs
git add src/MSOSync.App/SignalR/SyncOperationsPublisher.cs
git add src/MSOSync.Security/SecurityServiceExtensions.cs
git add src/MSOSync.App/Program.cs
git add tests/MSOSync.AppTests/MSOSync.AppTests.csproj
git add tests/MSOSync.AppTests/SignalR/NodeOperationsPublisherTests.cs
git add tests/MSOSync.AppTests/SignalR/SyncOperationsPublisherTests.cs
git add MSOSync.sln
git commit -m "feat(11C): add SignalR hub, publishers, JWT hub auth, AppTests"
```

## Report Contract

Return: `DONE`, last commit SHA, test results (count + all pass), build result (exit 0), any concerns. Write full report to the report file path provided by the coordinator.
