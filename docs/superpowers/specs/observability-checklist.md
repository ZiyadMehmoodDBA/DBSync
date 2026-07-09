# Observability Validation Checklist

Every long-running or async operation must produce ALL of the following signals:

| Signal | Required |
|--------|----------|
| Audit event (sync_audit row) | ✓ |
| CorrelationId on audit event | ✓ |
| SignalR broadcast | ✓ |
| Structured log with operation summary | ✓ |
| Structured log on failure | ✓ |
| Duration metric (or log with timing) | ✓ |

## Operations to validate

### Export Jobs (ExportJobService + ExportJobWorker)
- [ ] Audit event on job creation: action = EXPORT_JOB_CREATED
- [ ] Audit event on completion: action = EXPORT_JOB_COMPLETED
- [ ] SignalR ExportJobChanged broadcast on completion
- [ ] Structured log: LogInformation on start, LogError on failure
- [ ] Duration logged

### Configuration Rollout (RolloutService)
- [ ] Audit event on start: action = ROLLOUT_STARTED
- [ ] Audit event on completion: action = ROLLOUT_COMPLETED
- [ ] SignalR ConfigurationChanged on completion
- [ ] CorrelationId = rolloutId.ToString()
- [ ] Duration: CompletedAt - StartedAt derivable from DB row

### Node Decommission (NodeLifecycleService)
- [ ] Audit event on initiation: action = NODE_DECOMMISSION_INITIATED
- [ ] Audit event on completion: action = NODE_DECOMMISSION_COMPLETED
- [ ] SignalR NodeLifecycleChanged broadcast
- [ ] Structured log on each drain check
- [ ] CorrelationId propagated through lifecycle history

## Sign-off

Reviewer: ________________  Date: ________________  
All gaps fixed: [ ] YES  [ ] NO (list open items)
