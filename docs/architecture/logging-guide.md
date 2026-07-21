# Logging Guide

MSOSync uses `Microsoft.Extensions.Logging` with `ILogger<T>` injected via
primary constructor. All log messages use structured parameters. Plugins log
through `IPluginLogger` (`PluginLoggerAdapter` bridges to `ILogger<T>`).

## Log Level Policy

| Level | When to Use | Examples |
|---|---|---|
| `LogDebug` | High-frequency routine work. Per-tick, per-item, lock contention, idempotent skips | Worker tick body, empty pull results, lock held skip, duplicate batch ack, probe success per-node |
| `LogInformation` | Significant events worth seeing in production at normal verbosity | State transitions, work summaries, recovery completion, mode changes, job start/complete, worker disabled at startup |
| `LogWarning` | Recoverable anomalies — needs attention but not paging | Auth mismatches, sequence gaps, stale config, transport failures, concurrency guards triggered, malformed request payloads |
| `LogError` | Requires operator attention. Worker crashes, startup failures, unhandled exceptions | Worker tick failures, export job failures, startup bootstrapper failures, unhandled 500 errors, batch apply fatal errors |
| `LogCritical` | (not used) | Reserved for future use (e.g., unrecoverable DB failure) |

## Rules

- **RULE-LOG-1:** No string concatenation in log calls. Always use named structured parameters:
  ```csharp
  // WRONG
  logger.LogInformation("Node " + nodeId + " activated");

  // CORRECT
  logger.LogInformation("Node {NodeId} activated", nodeId);
  ```

- **RULE-LOG-2:** `LogError` is for exceptions requiring operator attention. Not for expected flow.
  ```csharp
  // WRONG — expected business path
  logger.LogError("User {Username} not found", username);

  // CORRECT — unexpected failure
  logger.LogError(ex, "PullJob tick failed");
  ```

- **RULE-LOG-3:** `LogWarning` for recoverable anomalies — things that should not happen in steady state but are recoverable without data loss.

- **RULE-LOG-4:** `LogInformation` only for state transitions and work completions. Not for per-tick routine work.

- **RULE-LOG-5:** `LogDebug` for anything that fires more than once per second in steady state.

## Naming Convention

Use a PascalCase component prefix in the message template to aid filtering:

```csharp
logger.LogInformation("PullJob: {Count} batches applied from {NodeId}", count, nodeId);
logger.LogWarning("ProbeWorker: {NodeId} unreachable after {Ms}ms", nodeId, latencyMs);
logger.LogError(ex, "ExportJobWorker: job {JobId} failed", jobId);
```

## Exception Logging

Always pass the exception as the first argument so structured log sinks
capture the full stack trace:

```csharp
// CORRECT
logger.LogError(ex, "PullJob tick failed");

// WRONG — exception lost
logger.LogError("PullJob tick failed: {Message}", ex.Message);
```

## Representative Examples (from the codebase)

### LogDebug
```csharp
// Idempotent operation (SyncController)
logger.LogDebug("Push: duplicate batch source={Source} seq={Seq} — returning 200", source, seq);

// Wall-clock sleep (PurgeJob)
logger.LogDebug("PurgeJob sleeping {Delay} until next 02:00 UTC", delay);
```

### LogInformation
```csharp
// State transition (DecommissionWorker)
logger.LogInformation("Node {NodeId} decommission finalized ({Reason})", node.NodeId, decision.Reason);

// Work summary (RetryJob — only when count > 0)
logger.LogInformation("RetryJob queued {Count} batches for retry", count);

// One-time startup mode message (PullJob)
logger.LogInformation("PullJob disabled — node {NodeId} is in Push mode", props.NodeId);

// Recovery event (SchedulerRecovery)
logger.LogInformation("Recovery {Reason}: Batch {BatchId} Sending→Error (stale {SentTime})", reason, id, sentTime);
```

### LogWarning
```csharp
// Auth mismatch (SyncController)
logger.LogWarning("Push: TargetNodeId {Target} != local nodeId {Me}", payload.TargetNodeId, ownNodeId);

// Malformed payload boundary guard (SyncController)
logger.LogWarning(ex, "Push: failed to decompress/deserialize request body");
```

### LogError
```csharp
// Worker tick failure (all Scheduler workers)
logger.LogError(ex, "PullJob tick failed");

// Individual job failure (ExportJobWorker)
logger.LogError(ex, "Export job {JobId} failed", job.JobId);

// Startup failure (AdminBootstrapper)
logger.LogError(ex, "AdminBootstrapper failed during startup — admin user may not be available");

// Fatal batch apply (ApplyEngine)
logger.LogError(ex, "ApplyEngine: fatal error on batch {BatchId}", incoming.BatchId);
```

## Audit Result (Phase 2A.7, 2026-07-21)

- String-concatenation scan: no matches in log calls.
- `LogError` scan: 15 sites, all on genuine error paths (worker tick/cycle
  failures, startup failures, unhandled exceptions, transport/apply fatal
  errors). No business-flow `LogError`.
- Scheduler `LogInformation` scan: all sites are one-time startup messages,
  state transitions, work summaries, or recovery events. No per-tick
  information logging.
