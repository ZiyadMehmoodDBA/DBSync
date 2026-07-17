# Phase 2A.7 — Logging Consistency

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Audit log levels across the codebase and document the logging convention. The audit found logging is consistent and well-structured — all structured logging uses named parameters, levels align with severity, and `GlobalExceptionHandler` is the only `LogError` site for unhandled exceptions. This plan produces the reference document.

**Architecture:** All logging uses `ILogger<T>` injected via primary constructor. Structured logging throughout — named parameters in all templates (`{NodeId}`, `{BatchId}`, etc.). Four levels in use: `LogDebug` (routine ticks, lock contention, idempotent ops), `LogInformation` (state transitions, work completions), `LogWarning` (auth mismatches, data integrity gaps, transport failures), `LogError` (worker tick failures, startup failures, unhandled exceptions).

**Tech Stack:** C# 13 / .NET 9 / Microsoft.Extensions.Logging / Serilog (if wired)

## Global Constraints

- No new product features. Scope is strictly audit and documentation.
- Definition of Complete: log level scan passed + docs committed + `dotnet test` exits 0.
- RULE-LOG-1: All log messages use structured logging with named parameters — no string concatenation.
- RULE-LOG-2: `LogError` only for exceptions that require operator attention (worker failures, startup failures, unhandled exceptions). Not for expected business flow.
- RULE-LOG-3: `LogWarning` for recoverable anomalies: auth mismatches, data integrity gaps, config warnings, transport failures.
- RULE-LOG-4: `LogInformation` for significant state transitions and work completions — not for per-tick routine work.
- RULE-LOG-5: `LogDebug` for high-frequency routine work: ticks, lock contention, empty results, idempotent operations.

---

## File Map

**Create:**
- `docs/architecture/logging-guide.md`

---

## Task 1: Verify Log Level Consistency and Write Logging Guide

- [ ] **Step 1: Scan for string concatenation in log calls**

```powershell
grep -rn "Log(Information|Warning|Error|Debug|Critical).*\+" D:\MSOSync\src\ --include="*.cs"
```

Expected: No matches. All log messages use structured parameters, not concatenation.

If any matches found, convert them:
```csharp
// BAD — string concatenation:
logger.LogInformation("Node " + nodeId + " activated");

// GOOD — structured parameter:
logger.LogInformation("Node {NodeId} activated", nodeId);
```

- [ ] **Step 2: Scan for LogError on non-exception paths**

```powershell
grep -rn "LogError" D:\MSOSync\src\ --include="*.cs" -B 1 -A 1
```

Review every `LogError` call. It should only appear for:
- Unhandled exceptions (in `GlobalExceptionHandler`)
- Worker tick failures (`catch (Exception ex) { logger.LogError(ex, ...) }`)
- Startup failures (`AdminBootstrapper`, `LifecycleStartupValidator`)
- Data consistency errors (e.g., terminal node with `MaintenanceMode=true`)

If any `LogError` appears for expected business flow (e.g., "user not found", "permission denied"), change it to `LogWarning`.

- [ ] **Step 3: Scan for LogInformation in tight loops or per-tick paths**

```powershell
grep -rn "LogInformation" D:\MSOSync\src\MSOSync.Scheduler\ --include="*.cs" -A 2
```

Review for any `LogInformation` inside hot paths (per-batch, per-tick loops). These should be `LogDebug`. 

Expected findings:
- `PullJob.cs` line 35: "PullJob disabled — node {NodeId} is in Push mode" — LogInformation is correct here (mode change, not per-tick)
- `RetryJob.cs` line 29: "RetryJob queued {Count} batches for retry" — LogInformation correct (work summary)
- Per-batch operations and empty-result checks should be LogDebug — verify these are already at Debug level.

- [ ] **Step 4: Create logging-guide.md**

Create `docs/architecture/logging-guide.md`:

```markdown
# Logging Guide

MSOSync uses `Microsoft.Extensions.Logging` with `ILogger<T>` injected via
primary constructor. All log messages use structured parameters.

## Log Level Policy

| Level | When to Use | Examples |
|---|---|---|
| `LogDebug` | High-frequency routine work. Per-tick, per-item, lock contention, idempotent skips | Worker tick body, empty pull results, lock held skip, duplicate batch ack, probe success per-node |
| `LogInformation` | Significant events worth seeing in production at normal verbosity | State transitions, work summaries, recovery completion, mode changes, job start/complete |
| `LogWarning` | Recoverable anomalies — needs attention but not paging | Auth mismatches, sequence gaps, stale config, transport failures, concurrency guards triggered, security config warnings |
| `LogError` | Requires operator attention. Worker crashes, startup failures, unhandled exceptions | Worker tick failures, export job failures, startup bootstrapper failures, unhandled 500 errors |
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
  ```csharp
  // WRONG — per-tick in tight loop
  logger.LogInformation("SyncJob tick started");

  // CORRECT — for high-frequency tick context
  logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
  ```

- **RULE-LOG-5:** `LogDebug` for anything that fires more than once per second in steady state.

## Naming Convention

Use PascalCase component prefix in the message template to aid filtering:

```csharp
logger.LogInformation("PullJob: {Count} batches applied from {NodeId}", count, nodeId);
logger.LogWarning("ProbeWorker: {NodeId} unreachable after {Ms}ms", nodeId, latencyMs);
logger.LogError(ex, "ExportJobWorker: job {JobId} failed", jobId);
```

## Exception Logging

Always pass the exception as the first argument so structured log sinks capture the full stack trace:

```csharp
// CORRECT
logger.LogError(ex, "PullJob tick failed");

// WRONG — exception lost
logger.LogError("PullJob tick failed: {Message}", ex.Message);
```

## Representative Examples

### LogDebug
```csharp
// Routine tick - lock contention
logger.LogDebug("SyncJob: lock held by another instance, skipping tick");

// Idempotent operation
logger.LogDebug("PullJob: duplicate batch source={Source} seq={Seq} — sending idempotent ACK", source.NodeId, batch.BatchSequence);

// Empty result
logger.LogDebug("PullJob: no batches from {Source} channel {Ch}", source.NodeId, channelId);

// Optimistic concurrency loss
logger.LogDebug("ConnectivityEvaluator lost a concurrency race; next cycle re-evaluates");
```

### LogInformation
```csharp
// State transition
logger.LogInformation("Node {NodeId} decommission finalized ({Reason})", node.NodeId, decision.Reason);

// Work summary
logger.LogInformation("RetryJob queued {Count} batches for retry", count);

// Recovery summary
logger.LogInformation("SchedulerRecovery complete: sendingRecovered={S} retryRequeued={R} newRecovered={N}", s, r, n);
```

### LogWarning
```csharp
// Auth mismatch
logger.LogWarning("Pull: TargetNodeId {Target} != authenticated nodeId {Me}", req.TargetNodeId, myNodeId);

// Data integrity gap
logger.LogWarning("PullJob: sequence gap from {Source} channel {Ch}: expected {Exp} got {Got}", source.NodeId, batch.ChannelId, lastSeq + 1, batch.BatchSequence);

// Concurrency guard
logger.LogWarning("ConnectivityEvaluator cycle skipped — previous evaluation still running");
```

### LogError
```csharp
// Worker tick failure
logger.LogError(ex, "PullJob tick failed");

// Individual job failure
logger.LogError(ex, "Export job {JobId} failed", job.JobId);

// Startup failure
logger.LogError(ex, "AdminBootstrapper failed during startup — admin user may not be available");
```
```

- [ ] **Step 5: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add docs/architecture/logging-guide.md
git commit -m "docs(2A.7): logging guide and log level policy"
```

---

## Completion Criteria

2A.7 is **Complete** when:
1. `grep -rn "Log.*\+" src/ --include="*.cs"` returns no log calls with string concatenation.
2. All `LogError` calls are for genuine error paths (exceptions, startup failures, worker crashes).
3. `dotnet test` exits 0.
4. `docs/architecture/logging-guide.md` committed with accurate level policy.
