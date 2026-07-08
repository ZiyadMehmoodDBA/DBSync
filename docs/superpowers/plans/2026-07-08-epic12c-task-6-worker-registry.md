# Epic 12C Task 6: IWorkerStatusRegistry + WorkerStatusDto + TickRecord

**Goal:** Create an in-memory singleton that all background workers call to self-report execution health. The registry tracks state, tick history, failure counts, and fires MediatR events when health state transitions.

---

## Step 1: Create WorkerStatusDto.cs

- [ ] Create file `src/MSOSync.App/Workers/WorkerStatusDto.cs`
- [ ] Paste the following content exactly:

```csharp
using MSOSync.App.Workers;

namespace MSOSync.App.Workers;

public enum WorkerState { Running, Idle, Warning, Delayed, Failed, Disabled }
public enum WorkerExecutionState { Running, Idle }
public enum WorkerHealthState { Healthy, Warning, Delayed, Failed, Disabled }
public enum TickTrigger { Scheduled, Manual, Startup, Retry }

public sealed record TickRecord(
    DateTime StartedAt,
    DateTime CompletedAt,
    long DurationMs,
    bool Success,
    string? Error,
    TickTrigger Trigger);

public sealed record WorkerStatusDto(
    string WorkerName,
    string WorkerVersion,
    TimeSpan ExpectedInterval,
    DateTime RegisteredAt,
    bool Enabled,
    WorkerState State,
    WorkerExecutionState ExecutionState,
    WorkerHealthState HealthState,
    DateTime? LastStarted,
    DateTime? LastCompleted,
    DateTime? LastSuccessfulRun,
    DateTime? NextExpected,
    long AverageDurationMs,
    long LastDurationMs,
    long ExecutionCount,
    int ConsecutiveFailures,
    string? LastError,
    DateTime LastHeartbeat,
    double SuccessRatePct,
    long MaxDurationMs,
    int FailureCount,
    DateTime? LastFailureAt,
    TickRecord[] RecentTicks);
```

---

## Step 2: Create IWorkerStatusRegistry.cs

- [ ] Create file `src/MSOSync.App/Workers/IWorkerStatusRegistry.cs`
- [ ] Paste the following content exactly:

```csharp
namespace MSOSync.App.Workers;

public interface IWorkerStatusRegistry
{
    void Register(string workerName, TimeSpan expectedInterval);
    void RecordTickStart(string workerName, TickTrigger trigger = TickTrigger.Scheduled);
    void RecordTickComplete(string workerName);
    void RecordTickFailed(string workerName, Exception ex);
    WorkerStatusDto GetOne(string workerName);
    WorkerStatusDto[] GetAll();
}
```

---

## Step 3: Create WorkerStatusChangedEvent.cs

- [ ] Create file `src/MSOSync.App/SignalR/WorkerStatusChangedEvent.cs`
- [ ] Paste the following content exactly:

```csharp
using MediatR;
using MSOSync.App.Workers;

namespace MSOSync.App.SignalR;

public sealed record WorkerStatusChangedEvent(
    string WorkerName,
    WorkerHealthState PreviousState,
    WorkerHealthState NewState,
    DateTime OccurredAt) : INotification;
```

---

## Step 4: Add WorkerStatusChanged to OperationsEventType enum

- [ ] Open `src/MSOSync.App/SignalR/OperationsEventType.cs` (or wherever the enum is defined — search for `OperationsEventType` if unsure)
- [ ] Add the following entry at the end of the enum body:

```csharp
WorkerStatusChanged,
```

Example result:
```csharp
public enum OperationsEventType
{
    // ... existing entries ...
    WorkerStatusChanged,
}
```

---

## Step 5: Create WorkerStatusRegistry.cs

- [ ] Create file `src/MSOSync.App/Workers/WorkerStatusRegistry.cs`
- [ ] Paste the following content exactly:

```csharp
using System.Collections.Concurrent;
using MediatR;
using MSOSync.App.SignalR;

namespace MSOSync.App.Workers;

public sealed class WorkerStatusRegistry(IPublisher publisher) : IWorkerStatusRegistry
{
    private sealed class WorkerEntry
    {
        public string WorkerName { get; init; } = "";
        public TimeSpan ExpectedInterval { get; init; }
        public DateTime RegisteredAt { get; } = DateTime.UtcNow;
        public bool Enabled { get; set; } = true;

        // Tick state — protected by _lock
        private readonly object _lock = new();
        public DateTime? LastStarted { get; private set; }
        public DateTime? LastCompleted { get; private set; }
        public DateTime? LastSuccessfulRun { get; private set; }
        public DateTime? LastFailureAt { get; private set; }
        public long ExecutionCount { get; private set; }
        public int ConsecutiveFailures { get; private set; }
        public int FailureCount { get; private set; }
        public string? LastError { get; private set; }
        public DateTime LastHeartbeat { get; private set; } = DateTime.UtcNow;
        public DateTime? CurrentTickStartedAt { get; private set; }
        public TickTrigger CurrentTickTrigger { get; private set; }

        // Rolling stats (lock-protected)
        private long _totalDurationMs;
        private long _maxDurationMs;
        private long _successCount;

        // Tick history — ConcurrentQueue, capped at 100
        private readonly ConcurrentQueue<TickRecord> _ticks = new();

        public void RecordStart(TickTrigger trigger)
        {
            lock (_lock)
            {
                CurrentTickStartedAt = DateTime.UtcNow;
                CurrentTickTrigger = trigger;
                LastStarted = CurrentTickStartedAt;
                LastHeartbeat = DateTime.UtcNow;
            }
        }

        public TickRecord RecordComplete()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var startedAt = CurrentTickStartedAt ?? now;
                var durationMs = (long)(now - startedAt).TotalMilliseconds;

                ExecutionCount++;
                _successCount++;
                ConsecutiveFailures = 0;
                LastCompleted = now;
                LastSuccessfulRun = now;
                LastHeartbeat = now;
                CurrentTickStartedAt = null;
                _totalDurationMs += durationMs;
                if (durationMs > _maxDurationMs) _maxDurationMs = durationMs;

                var tick = new TickRecord(startedAt, now, durationMs, true, null, CurrentTickTrigger);
                EnqueueTick(tick);
                return tick;
            }
        }

        public TickRecord RecordFailed(Exception ex)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var startedAt = CurrentTickStartedAt ?? now;
                var durationMs = (long)(now - startedAt).TotalMilliseconds;

                ExecutionCount++;
                ConsecutiveFailures++;
                FailureCount++;
                LastCompleted = now;
                LastFailureAt = now;
                LastError = ex.Message;
                LastHeartbeat = now;
                CurrentTickStartedAt = null;
                _totalDurationMs += durationMs;
                if (durationMs > _maxDurationMs) _maxDurationMs = durationMs;

                var tick = new TickRecord(startedAt, now, durationMs, false, ex.Message, CurrentTickTrigger);
                EnqueueTick(tick);
                return tick;
            }
        }

        public WorkerStatusSnapshot Snapshot()
        {
            lock (_lock)
            {
                var avgMs = ExecutionCount > 0 ? _totalDurationMs / ExecutionCount : 0;
                var lastDurationMs = _ticks.TryPeek(out var last) ? 0L : 0L; // computed below
                var ticks = _ticks.ToArray();
                var lastDur = ticks.Length > 0 ? ticks[^1].DurationMs : 0L;
                var successRate = ExecutionCount > 0 ? (_successCount * 100.0) / ExecutionCount : 100.0;
                return new WorkerStatusSnapshot(
                    LastStarted, LastCompleted, LastSuccessfulRun, LastFailureAt,
                    ExecutionCount, ConsecutiveFailures, FailureCount, LastError,
                    LastHeartbeat, avgMs, lastDur, _maxDurationMs, successRate,
                    CurrentTickStartedAt, ticks, Enabled);
            }
        }

        private void EnqueueTick(TickRecord tick)
        {
            _ticks.Enqueue(tick);
            while (_ticks.Count > 100)
                _ticks.TryDequeue(out _);
        }
    }

    private sealed record WorkerStatusSnapshot(
        DateTime? LastStarted, DateTime? LastCompleted, DateTime? LastSuccessfulRun,
        DateTime? LastFailureAt, long ExecutionCount, int ConsecutiveFailures, int FailureCount,
        string? LastError, DateTime LastHeartbeat, long AverageDurationMs, long LastDurationMs,
        long MaxDurationMs, double SuccessRatePct, DateTime? CurrentTickStartedAt,
        TickRecord[] RecentTicks, bool Enabled);

    private readonly ConcurrentDictionary<string, WorkerEntry> _entries = new();
    private readonly ConcurrentDictionary<string, WorkerHealthState> _lastHealthState = new();

    public void Register(string workerName, TimeSpan expectedInterval)
    {
        _entries.TryAdd(workerName, new WorkerEntry
        {
            WorkerName = workerName,
            ExpectedInterval = expectedInterval
        });
        _lastHealthState.TryAdd(workerName, WorkerHealthState.Healthy);
    }

    public void RecordTickStart(string workerName, TickTrigger trigger = TickTrigger.Scheduled)
    {
        if (_entries.TryGetValue(workerName, out var entry))
            entry.RecordStart(trigger);
    }

    public void RecordTickComplete(string workerName)
    {
        if (!_entries.TryGetValue(workerName, out var entry)) return;
        entry.RecordComplete();
        CheckAndPublishTransition(workerName, entry);
    }

    public void RecordTickFailed(string workerName, Exception ex)
    {
        if (!_entries.TryGetValue(workerName, out var entry)) return;
        entry.RecordFailed(ex);
        CheckAndPublishTransition(workerName, entry);
    }

    public WorkerStatusDto GetOne(string workerName)
    {
        if (!_entries.TryGetValue(workerName, out var entry))
            throw new KeyNotFoundException($"Worker '{workerName}' is not registered.");
        return BuildDto(workerName, entry);
    }

    public WorkerStatusDto[] GetAll()
        => _entries.Select(kv => BuildDto(kv.Key, kv.Value)).ToArray();

    private WorkerStatusDto BuildDto(string workerName, WorkerEntry entry)
    {
        var snap = entry.Snapshot();
        var now = DateTime.UtcNow;

        // Derive execution state
        WorkerExecutionState execState = snap.CurrentTickStartedAt.HasValue
            ? WorkerExecutionState.Running
            : WorkerExecutionState.Idle;

        // Derive health state
        WorkerHealthState healthState;
        if (!entry.Enabled)
        {
            healthState = WorkerHealthState.Disabled;
        }
        else if (snap.ConsecutiveFailures >= 5)
        {
            healthState = WorkerHealthState.Failed;
        }
        else if (snap.ConsecutiveFailures >= 3)
        {
            healthState = WorkerHealthState.Warning;
        }
        else if (snap.LastCompleted.HasValue && (now - snap.LastCompleted.Value) > entry.ExpectedInterval * 3)
        {
            healthState = WorkerHealthState.Delayed;
        }
        else if (!snap.LastCompleted.HasValue && (now - entry.RegisteredAt) > entry.ExpectedInterval * 2)
        {
            healthState = WorkerHealthState.Warning;
        }
        else
        {
            healthState = WorkerHealthState.Healthy;
        }

        // Derive combined state
        WorkerState state = healthState switch
        {
            WorkerHealthState.Disabled => WorkerState.Disabled,
            WorkerHealthState.Failed => WorkerState.Failed,
            WorkerHealthState.Warning => WorkerState.Warning,
            WorkerHealthState.Delayed => WorkerState.Delayed,
            _ => execState == WorkerExecutionState.Running ? WorkerState.Running : WorkerState.Idle
        };

        DateTime? nextExpected = snap.LastCompleted.HasValue
            ? snap.LastCompleted.Value + entry.ExpectedInterval
            : null;

        return new WorkerStatusDto(
            WorkerName: workerName,
            WorkerVersion: "1.0",
            ExpectedInterval: entry.ExpectedInterval,
            RegisteredAt: entry.RegisteredAt,
            Enabled: snap.Enabled,
            State: state,
            ExecutionState: execState,
            HealthState: healthState,
            LastStarted: snap.LastStarted,
            LastCompleted: snap.LastCompleted,
            LastSuccessfulRun: snap.LastSuccessfulRun,
            NextExpected: nextExpected,
            AverageDurationMs: snap.AverageDurationMs,
            LastDurationMs: snap.LastDurationMs,
            ExecutionCount: snap.ExecutionCount,
            ConsecutiveFailures: snap.ConsecutiveFailures,
            LastError: snap.LastError,
            LastHeartbeat: snap.LastHeartbeat,
            SuccessRatePct: snap.SuccessRatePct,
            MaxDurationMs: snap.MaxDurationMs,
            FailureCount: snap.FailureCount,
            LastFailureAt: snap.LastFailureAt,
            RecentTicks: snap.RecentTicks);
    }

    private void CheckAndPublishTransition(string workerName, WorkerEntry entry)
    {
        var dto = BuildDto(workerName, entry);
        var newState = dto.HealthState;
        var prevState = _lastHealthState.GetOrAdd(workerName, WorkerHealthState.Healthy);

        if (newState != prevState)
        {
            _lastHealthState[workerName] = newState;
            // Fire-and-forget; we're in a sync context
            _ = publisher.Publish(new WorkerStatusChangedEvent(
                workerName, prevState, newState, DateTime.UtcNow));
        }
    }
}
```

---

## Step 6: Create WorkerStatusChangedPublisher.cs

- [ ] Create file `src/MSOSync.App/SignalR/WorkerStatusChangedPublisher.cs`
- [ ] Paste the following content exactly:

```csharp
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace MSOSync.App.SignalR;

public sealed class WorkerStatusChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<WorkerStatusChangedEvent>
{
    public async Task Handle(WorkerStatusChangedEvent n, CancellationToken ct)
        => await hub.Clients.Group("operators").SendAsync(
            "WorkerStatusChanged",
            new
            {
                n.WorkerName,
                PreviousState = n.PreviousState.ToString(),
                NewState = n.NewState.ToString(),
                n.OccurredAt
            },
            ct);
}
```

---

## Step 7: Register IWorkerStatusRegistry in Program.cs

- [ ] Open `src/MSOSync.App/Program.cs`
- [ ] Find the section where singletons or worker services are registered (look for `AddHostedService` or a comment like `// Workers`)
- [ ] Add the following line BEFORE any worker `AddHostedService` registrations so the registry is available at startup:

```csharp
builder.Services.AddSingleton<IWorkerStatusRegistry, WorkerStatusRegistry>();
```

- [ ] Add the using directive at the top of the file if not already present:

```csharp
using MSOSync.App.Workers;
```

---

## Step 8: Create unit tests

- [ ] Check whether `tests/MSOSync.AppTests/` exists. If it does, add a new file there. If it does not exist, create the directory and a `.csproj` file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.App\MSOSync.App.csproj" />
  </ItemGroup>
</Project>
```

Then add the project to the solution:
```
dotnet sln add tests/MSOSync.AppTests/MSOSync.AppTests.csproj
```

- [ ] Create `tests/MSOSync.AppTests/Workers/WorkerRegistryTests.cs` with the following content:

```csharp
using MediatR;
using MSOSync.App.SignalR;
using MSOSync.App.Workers;
using NSubstitute;
using Xunit;

namespace MSOSync.AppTests.Workers;

public sealed class WorkerRegistryTests
{
    private static WorkerStatusRegistry CreateRegistry(out IPublisher publisher)
    {
        publisher = Substitute.For<IPublisher>();
        return new WorkerStatusRegistry(publisher);
    }

    // Test 1: Register + RecordTickStart => state = Running
    [Fact]
    public void RecordTickStart_AfterRegister_StateIsRunning()
    {
        var registry = CreateRegistry(out _);
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        registry.RecordTickStart("TestWorker");

        var dto = registry.GetOne("TestWorker");
        Assert.Equal(WorkerExecutionState.Running, dto.ExecutionState);
        Assert.Equal(WorkerState.Running, dto.State);
    }

    // Test 2: RecordTickComplete => state = Idle, LastCompleted set
    [Fact]
    public void RecordTickComplete_SetsIdleAndLastCompleted()
    {
        var registry = CreateRegistry(out _);
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        registry.RecordTickStart("TestWorker");
        registry.RecordTickComplete("TestWorker");

        var dto = registry.GetOne("TestWorker");
        Assert.Equal(WorkerExecutionState.Idle, dto.ExecutionState);
        Assert.NotNull(dto.LastCompleted);
        Assert.NotNull(dto.LastSuccessfulRun);
    }

    // Test 3: RecordTickFailed 3 times => HealthState = Warning
    [Fact]
    public void RecordTickFailed_ThreeTimes_HealthStateIsWarning()
    {
        var registry = CreateRegistry(out _);
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        for (int i = 0; i < 3; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickFailed("TestWorker", new Exception("boom"));
        }

        var dto = registry.GetOne("TestWorker");
        Assert.Equal(WorkerHealthState.Warning, dto.HealthState);
        Assert.Equal(WorkerState.Warning, dto.State);
    }

    // Test 4: RecordTickFailed 5 times => HealthState = Failed
    [Fact]
    public void RecordTickFailed_FiveTimes_HealthStateIsFailed()
    {
        var registry = CreateRegistry(out _);
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        for (int i = 0; i < 5; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickFailed("TestWorker", new Exception("boom"));
        }

        var dto = registry.GetOne("TestWorker");
        Assert.Equal(WorkerHealthState.Failed, dto.HealthState);
        Assert.Equal(WorkerState.Failed, dto.State);
    }

    // Test 5: Never tick after 2x interval => HealthState = Warning
    [Fact]
    public void NeverTicked_After2xInterval_HealthStateIsWarning()
    {
        // We cannot manipulate time directly, so we use a very short interval
        // and rely on the registry being constructed before the interval elapses.
        // Because the registry sets RegisteredAt = UtcNow, we set interval to 0
        // so that 2x = 0 and the condition is immediately satisfied.
        var registry = CreateRegistry(out _);
        registry.Register("NeverStartedWorker", TimeSpan.Zero);

        var dto = registry.GetOne("NeverStartedWorker");
        // With interval=0: 2x=0, so now - registeredAt (>0) > 0 => Warning
        Assert.Equal(WorkerHealthState.Warning, dto.HealthState);
    }

    // Test 6: GetAll returns all registered workers
    [Fact]
    public void GetAll_ReturnsAllRegisteredWorkers()
    {
        var registry = CreateRegistry(out _);
        registry.Register("WorkerA", TimeSpan.FromSeconds(10));
        registry.Register("WorkerB", TimeSpan.FromSeconds(20));
        registry.Register("WorkerC", TimeSpan.FromSeconds(30));

        var all = registry.GetAll();
        Assert.Equal(3, all.Length);
        Assert.Contains(all, w => w.WorkerName == "WorkerA");
        Assert.Contains(all, w => w.WorkerName == "WorkerB");
        Assert.Contains(all, w => w.WorkerName == "WorkerC");
    }

    // Test 7: Rolling history capped at 100 ticks
    [Fact]
    public void RecentTicks_CappedAt100()
    {
        var registry = CreateRegistry(out _);
        registry.Register("TestWorker", TimeSpan.FromSeconds(5));

        for (int i = 0; i < 150; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickComplete("TestWorker");
        }

        var dto = registry.GetOne("TestWorker");
        Assert.Equal(100, dto.RecentTicks.Length);
    }

    // Test 8: State transition fires WorkerStatusChangedEvent
    [Fact]
    public async Task RecordTickFailed_TransitionToWarning_PublishesEvent()
    {
        var registry = CreateRegistry(out var publisher);
        registry.Register("TestWorker", TimeSpan.FromSeconds(30));

        // Trigger 3 failures to cross Healthy -> Warning threshold
        for (int i = 0; i < 3; i++)
        {
            registry.RecordTickStart("TestWorker");
            registry.RecordTickFailed("TestWorker", new Exception("boom"));
        }

        // Wait briefly for fire-and-forget publish
        await Task.Delay(50);

        await publisher.Received().Publish(
            Arg.Is<WorkerStatusChangedEvent>(e =>
                e.WorkerName == "TestWorker" &&
                e.NewState == WorkerHealthState.Warning),
            Arg.Any<CancellationToken>());
    }
}
```

---

## Step 9: Build and verify

- [ ] Run `dotnet build src/MSOSync.App/MSOSync.App.csproj` — expect 0 errors
- [ ] Run `dotnet test tests/MSOSync.AppTests/MSOSync.AppTests.csproj` — expect 8 tests passing
- [ ] If the AppTests project did not exist before, also run `dotnet build MSOSync.sln` to verify the solution builds

---

## Acceptance criteria

- `IWorkerStatusRegistry` and `WorkerStatusRegistry` compile with no errors
- `WorkerStatusDto` includes all fields listed in the spec
- State transitions: 3 failures → Warning, 5 failures → Failed, never-started-past-2x-interval → Warning
- `WorkerStatusChangedPublisher` sends to `hub.Clients.Group("operators")` with the correct payload shape
- All 8 unit tests pass
