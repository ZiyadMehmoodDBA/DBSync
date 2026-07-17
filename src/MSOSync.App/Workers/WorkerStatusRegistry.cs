using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.App.SignalR;

namespace MSOSync.App.Workers;

public sealed class WorkerStatusRegistry(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkerStatusRegistry> logger) : IWorkerStatusRegistry
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
                CurrentTickTrigger = TickTrigger.Scheduled; // reset to default
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
                CurrentTickTrigger = TickTrigger.Scheduled; // reset to default
                return tick;
            }
        }

        public WorkerStatusSnapshot Snapshot()
        {
            lock (_lock)
            {
                var avgMs = ExecutionCount > 0 ? _totalDurationMs / ExecutionCount : 0;
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

        // Derive health state (rules applied in priority order)
        WorkerHealthState healthState;
        if (!snap.Enabled)
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
            WorkerHealthState.Failed   => WorkerState.Failed,
            WorkerHealthState.Warning  => WorkerState.Warning,
            WorkerHealthState.Delayed  => WorkerState.Delayed,
            _                          => execState == WorkerExecutionState.Running ? WorkerState.Running : WorkerState.Idle
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

        // Only publish if we win the CAS; prevents duplicate events under concurrent ticks
        if (newState != prevState && _lastHealthState.TryUpdate(workerName, newState, prevState))
        {
            var evt = new WorkerStatusChangedEvent(workerName, prevState, newState, DateTime.UtcNow);
            _ = Task.Run(async () =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var pub = scope.ServiceProvider.GetRequiredService<IPublisher>();
                try { await pub.Publish(evt); }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "WorkerStatusRegistry failed to publish {EventType} for worker {WorkerName}",
                        evt.GetType().Name, workerName);
                }
            });
        }
    }
}
