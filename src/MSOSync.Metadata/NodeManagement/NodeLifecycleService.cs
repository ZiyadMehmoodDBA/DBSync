using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.NodeManagement;

public sealed class RegistrationReceivedNotification(long registrationId) : INotification
{
    public long RegistrationId { get; } = registrationId;
}

/// <summary>
/// THE lifecycle mutation gateway (spec §4, Invariant 2): every write to
/// SyncNode.LifecycleState in the running system goes through this service
/// (the only exceptions are migration M022 and the startup validator).
/// </summary>
public sealed class NodeLifecycleService(
    AppDbContext                  db,
    IRegistrationDiffService      diffSvc,
    IAuditService                 auditSvc,
    IMediator                     mediator,
    INodeLifecycleStateMachine    stateMachine,
    INodeLifecycleHistoryService  history,
    IBootstrapTokenService        bootstrapTokens,
    NodeSecurityService           nodeSecurity,
    NodeLifecycleLockRegistry     locks,
    IOptions<LifecycleOptions>    options,
    IConfiguration                configuration,
    ILogger<NodeLifecycleService> logger) : INodeLifecycleService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── Pipeline core ──────────────────────────────────────────────────────────

    private async Task<Guid> ExecuteTransitionAsync(
        string nodeId,
        NodeLifecycleState target,
        LifecycleTrigger trigger,
        string actor,
        string? reason,
        string auditAction,
        Func<SyncNode, NodeLifecycleState, Task>? mutate = null,   // (node, previousState) — extra column writes inside the transaction
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        using var _ = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        using var @lock = await locks.AcquireAsync(nodeId, ct);

        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");

        // Revalidate at execution time — never trust pre-loaded state (spec §4.1)
        stateMachine.Validate(node.LifecycleState, target, correlationId);
        var previous = node.LifecycleState;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        node.LifecycleState = target;
        if (mutate is not null) await mutate(node, previous);

        await history.WriteTransitionAsync(new LifecycleTransitionRecord(
            nodeId, previous, target, trigger, reason, actor, correlationId, metadataJson), ct);

        try
        {
            await auditSvc.WriteAsync(auditAction, $"node:{nodeId} {previous}->{target} corr:{correlationId}", actor, ct);
            await db.SaveChangesAsync(ct);   // RowVersion check happens here
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException($"Node {nodeId} was modified concurrently");
        }
        await tx.CommitAsync(ct);

        // Publish AFTER commit — never before (spec §4.1)
        await mediator.Publish(new NodeLifecycleChangedEvent(nodeId, previous, target, trigger, correlationId), ct);
        return correlationId;
    }

    // ── Registration (12A, reworked) ───────────────────────────────────────────

    public async Task<long> RegisterAsync(InboundRegistrationDto dto, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Derive registration type. Decommissioned ExternalIds were freed at
            // finalization, so this lookup never matches for them — a returning
            // decommissioned identity registers as New automatically.
            var existingNode = await db.Nodes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.ExternalId == dto.ExternalId, ct);

            var regType = existingNode is null
                ? RegistrationType.New
                : existingNode.LifecycleState == NodeLifecycleState.Active
                    ? RegistrationType.ReRegistration
                    : RegistrationType.Recovery;

            // Validate and serialize metadata
            string? metadataJson = null;
            if (dto.Metadata is not null)
            {
                if (dto.Metadata.SchemaVersion < 1)
                    throw new ArgumentException("metadata.SchemaVersion must be >= 1");
                metadataJson = JsonSerializer.Serialize(dto.Metadata);
            }

            // For re-registrations with metadata, compute diff and store in audit detail
            string? diffSummary = null;
            if (regType != RegistrationType.New && existingNode is not null && dto.Metadata is not null)
            {
                var diff = diffSvc.Compute(dto.Metadata, existingNode);
                var changedCount = diff.Items.Count(i => i.ChangeType != RegistrationChangeType.Unchanged);
                if (changedCount > 0)
                    diffSummary = $"{changedCount} field(s) changed";
            }

            var request = new SyncRegistrationRequest
            {
                NodeId           = dto.ExternalId,
                NodeName         = dto.NodeName,
                DbType           = dto.NodeType,
                RequestTime      = DateTime.UtcNow,
                MetadataJson     = metadataJson,
                RegistrationType = regType,
                Status           = RegistrationStatus.Pending,
            };

            db.RegistrationRequests.Add(request);
            await db.SaveChangesAsync(ct);

            // Recovery: enter Recovery state via the pipeline. Duplicate re-registration
            // (node already Recovery) is an idempotent no-op — Invariant 11.
            if (regType == RegistrationType.Recovery
                && existingNode!.LifecycleState != NodeLifecycleState.Recovery)
            {
                await ExecuteTransitionAsync(existingNode.NodeId, NodeLifecycleState.Recovery, LifecycleTrigger.Recovery,
                    "system", "known ExternalId re-registered", NodeManagementAuditActions.NodeRecoveryRequested,
                    mutate: (node, previous) =>
                    {
                        node.PreviousLifecycleState ??= previous;   // captures PRE-transition state — Invariant 4
                        return Task.CompletedTask;
                    }, ct: ct);
            }

            var auditDetail = diffSummary is null
                ? $"Registration {request.RequestId} received for node {dto.ExternalId}"
                : $"Registration {request.RequestId} received for node {dto.ExternalId}. Diff: {diffSummary}";

            await auditSvc.WriteAsync(NodeManagementAuditActions.NodeRegistered, auditDetail, "system", ct);

            NodeManagementMetrics.RegistrationRequestsTotal.Add(1,
                new KeyValuePair<string, object?>("type", regType.ToString()),
                new KeyValuePair<string, object?>("status", "Pending"));

            await mediator.Publish(new RegistrationReceivedNotification(request.RequestId), ct);

            return request.RequestId;
        }
        finally
        {
            NodeManagementMetrics.RegistrationDuration.Record(sw.Elapsed.TotalSeconds);
        }
    }

    public async Task<ApproveResultDto> ApproveAsync(
        long id, string? notes, string actorUsername, CancellationToken ct = default)
    {
        var req = await db.RegistrationRequests
            .FirstOrDefaultAsync(r => r.RequestId == id, ct)
            ?? throw new NotFoundException($"Registration {id} not found.");

        if (req.Status == RegistrationStatus.Approved)
            throw new ConcurrencyException("Registration is already approved.");

        var bootstrapToken = await ApproveCoreAsync(req, notes, actorUsername, bulk: false, ct);
        return new ApproveResultDto(id, bootstrapToken);
    }

    /// <summary>
    /// Shared approval core (single + bulk): updates the registration row, then
    /// dispatches by RegistrationType. Returns the raw bootstrap token for
    /// Recovery approvals (null otherwise).
    /// </summary>
    private async Task<string?> ApproveCoreAsync(
        SyncRegistrationRequest req, string? notes, string actorUsername, bool bulk, CancellationToken ct)
    {
        req.Status      = RegistrationStatus.Approved;
        req.ProcessedAt = DateTime.UtcNow;
        req.ProcessedBy = actorUsername;
        req.Approved    = true;

        string? bootstrapToken = null;
        var approveAction = NodeManagementAuditActions.NodeApproved;

        switch (req.RegistrationType)
        {
            case RegistrationType.New:
                // 12A deferred item: approval creates the SyncNode in PendingRegistration.
                if (!await db.Nodes.AnyAsync(n => n.ExternalId == req.NodeId, ct))
                {
                    var metadata = req.MetadataJson is null
                        ? null
                        : JsonSerializer.Deserialize<RegistrationMetadataDto>(req.MetadataJson, JsonOpts);

                    db.Nodes.Add(new SyncNode
                    {
                        NodeId         = req.NodeId,
                        GroupId        = req.NodeGroup ?? "default",
                        SyncUrl        = req.SyncUrl ?? $"https://{req.NodeName}.local:8080",
                        LifecycleState = NodeLifecycleState.PendingRegistration,
                        NodeType       = req.DbType ?? string.Empty,
                        ExternalId     = req.NodeId,
                        NodeName       = req.NodeName,
                        DbServer       = metadata?.Machine?.HostName,
                        DbName         = metadata?.Database?.InstanceName,
                    });

                    // Entry into the canonical model: FromState = null (spec §2.2)
                    await history.WriteTransitionAsync(new LifecycleTransitionRecord(
                        req.NodeId, null, NodeLifecycleState.PendingRegistration,
                        LifecycleTrigger.Registration, notes, actorUsername, Guid.NewGuid()), ct);
                }
                break;

            case RegistrationType.ReRegistration:
                // No state change — node already Active (existing behavior).
                approveAction = NodeManagementAuditActions.NodeReRegistered;
                break;

            case RegistrationType.Recovery:
                // No state change (spec §2.2 note): node stays Recovery until activation.
                // Old identity dies first, then a fresh one-time bootstrap token is issued.
                var node = await db.Nodes.FirstOrDefaultAsync(n => n.ExternalId == req.NodeId, ct)
                    ?? throw new NotFoundException($"Node with ExternalId {req.NodeId} not found", "NODE_NOT_FOUND");
                await nodeSecurity.RevokeAsync(node.NodeId, ct);
                bootstrapToken = await bootstrapTokens.IssueAsync(node.NodeId, actorUsername, ct);
                approveAction = NodeManagementAuditActions.NodeRecoveryApproved;
                break;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Registration was modified concurrently.");
        }

        var detail = bulk
            ? $"Bulk: Registration {req.RequestId} approved by {actorUsername}"
            : $"Registration {req.RequestId} approved by {actorUsername}. Notes: {notes}";
        await auditSvc.WriteAsync(approveAction, detail, actorUsername, ct);

        NodeManagementMetrics.ApprovalsTotal.Add(1);
        return bootstrapToken;
    }

    public async Task RejectAsync(
        long id, string? reason, string actorUsername, CancellationToken ct = default)
    {
        var req = await db.RegistrationRequests
            .FirstOrDefaultAsync(r => r.RequestId == id, ct)
            ?? throw new NotFoundException($"Registration {id} not found.");

        if (req.Status == RegistrationStatus.Rejected)
            throw new ConcurrencyException("Registration is already rejected.");

        await RejectCoreAsync(req, reason, actorUsername, bulk: false, ct);
    }

    /// <summary>Shared rejection core (single + bulk); recovery reject returns the node to PreviousLifecycleState.</summary>
    private async Task RejectCoreAsync(
        SyncRegistrationRequest req, string? reason, string actorUsername, bool bulk, CancellationToken ct)
    {
        // Retry safety (Invariant 11): run the node lifecycle transition FIRST and only
        // mark the registration Rejected once it has succeeded. ExecuteTransitionAsync
        // owns its own transaction, so ordering the durable side-effect last means a
        // failed transition leaves the request Pending and rejectable again on retry.
        if (req.RegistrationType == RegistrationType.Recovery)
        {
            var node = await db.Nodes.FirstOrDefaultAsync(n => n.ExternalId == req.NodeId, ct)
                ?? throw new NotFoundException($"Node with ExternalId {req.NodeId} not found", "NODE_NOT_FOUND");
            if (node.LifecycleState == NodeLifecycleState.Recovery)
            {
                var target = node.PreviousLifecycleState
                    ?? NodeLifecycleState.Disabled;   // defensive: never happens per Invariant 4
                await ExecuteTransitionAsync(node.NodeId, target, LifecycleTrigger.Recovery,
                    actorUsername, reason, NodeManagementAuditActions.NodeRecoveryRejected,
                    mutate: (n, _) => { n.PreviousLifecycleState = null; return Task.CompletedTask; }, ct: ct);
            }
            // else: node already left Recovery (e.g. a prior attempt's transition committed
            // but the registration save failed) — skip the transition and just mark rejected.
        }

        req.Status      = RegistrationStatus.Rejected;
        req.ProcessedAt = DateTime.UtcNow;
        req.ProcessedBy = actorUsername;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Registration was modified concurrently.");
        }

        var detail = bulk
            ? $"Bulk: Registration {req.RequestId} rejected by {actorUsername}. Reason: {reason}"
            : $"Registration {req.RequestId} rejected by {actorUsername}. Reason: {reason}";
        await auditSvc.WriteAsync(NodeManagementAuditActions.NodeRejected, detail, actorUsername, ct);

        NodeManagementMetrics.RejectionsTotal.Add(1);
    }

    public async Task<IReadOnlyList<BulkResultItemDto>> BulkApproveAsync(
        IReadOnlyList<long> ids, string actorUsername, CancellationToken ct = default)
    {
        var sw      = Stopwatch.StartNew();
        var results = new List<BulkResultItemDto>();
        try
        {
            foreach (var id in ids)
            {
                try
                {
                    var req = await db.RegistrationRequests
                        .FirstOrDefaultAsync(r => r.RequestId == id, ct);
                    if (req is null) { results.Add(new BulkResultItemDto(id, "NotFound")); continue; }
                    if (req.Status == RegistrationStatus.Approved)
                    { results.Add(new BulkResultItemDto(id, "AlreadyApproved")); continue; }

                    // Bulk must never emit bootstrap tokens — recovery needs individual approval.
                    if (req.RegistrationType == RegistrationType.Recovery)
                    { results.Add(new BulkResultItemDto(id, "RequiresIndividualApproval")); continue; }

                    await ApproveCoreAsync(req, null, actorUsername, bulk: true, ct);
                    results.Add(new BulkResultItemDto(id, "Approved"));
                }
                catch (ConcurrencyException)
                {
                    results.Add(new BulkResultItemDto(id, "Conflict"));
                }
            }
        }
        finally
        {
            NodeManagementMetrics.BulkOperationDuration.Record(sw.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "approve"));
        }
        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<BulkResultItemDto>> BulkRejectAsync(
        IReadOnlyList<long> ids, string? reason, string actorUsername, CancellationToken ct = default)
    {
        var sw      = Stopwatch.StartNew();
        var results = new List<BulkResultItemDto>();
        try
        {
            foreach (var id in ids)
            {
                try
                {
                    var req = await db.RegistrationRequests
                        .FirstOrDefaultAsync(r => r.RequestId == id, ct);
                    if (req is null) { results.Add(new BulkResultItemDto(id, "NotFound")); continue; }
                    if (req.Status == RegistrationStatus.Rejected)
                    { results.Add(new BulkResultItemDto(id, "AlreadyRejected")); continue; }

                    await RejectCoreAsync(req, reason, actorUsername, bulk: true, ct);
                    results.Add(new BulkResultItemDto(id, "Rejected"));
                }
                catch (ConcurrencyException)
                {
                    results.Add(new BulkResultItemDto(id, "Conflict"));
                }
            }
        }
        finally
        {
            NodeManagementMetrics.BulkOperationDuration.Record(sw.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "reject"));
        }
        return results.AsReadOnly();
    }

    public async Task<ProvisionResultDto> ProvisionAsync(
        ProvisionRequestDto dto, string actorUsername, CancellationToken ct = default)
    {
        // Reuse an approve-created node in PendingRegistration (spec §4.4) — never a second node.
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.ExternalId == dto.ExternalId, ct);
        if (node is not null && node.LifecycleState != NodeLifecycleState.PendingRegistration)
            throw new DuplicateEntityException(
                $"Node with ExternalId '{dto.ExternalId}' already exists in state {node.LifecycleState}",
                "NODE_ALREADY_EXISTS");

        if (node is null)
        {
            node = new SyncNode
            {
                NodeId     = dto.ExternalId,
                GroupId    = dto.GroupId ?? "default",
                SyncUrl    = $"https://{dto.NodeName}.local:8080",
                LifecycleState = NodeLifecycleState.PendingRegistration,
                NodeType   = dto.NodeType,
                ExternalId = dto.ExternalId,
                NodeName   = dto.NodeName,
                DbServer   = dto.DbServer,
                DbName     = dto.DbName,
            };
            db.Nodes.Add(node);
        }

        // Stored one-time bootstrap token — activation validates against its hash.
        var token = await bootstrapTokens.IssueAsync(node.NodeId, actorUsername, ct);
        await db.SaveChangesAsync(ct);

        // Audit: write "token:issued" — never the token value
        await auditSvc.WriteAsync(NodeManagementAuditActions.NodeProvisioned,
            $"Node {node.NodeId} provisioned by {actorUsername}. token:issued",
            actorUsername, ct);

        return new ProvisionResultDto(node.NodeId, token);
    }

    // ── Node-facing activation ─────────────────────────────────────────────────

    public async Task<ActivateResultDto> ActivateAsync(
        string externalId, string bootstrapToken, string agentVersion, CancellationToken ct = default)
    {
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.ExternalId == externalId, ct)
            ?? throw new UnauthorizedException("Invalid activation credentials", "ACTIVATION_DENIED");
        // NOTE: 401 (not 404) for unknown ExternalId — do not leak node existence to unauthenticated callers.

        var correlationId = Guid.NewGuid();
        using var @lock = await locks.AcquireAsync(node.NodeId, ct);

        // Reload under lock — retry safety (Invariant 11)
        await db.Entry(node).ReloadAsync(ct);

        if (node.LifecycleState is not (NodeLifecycleState.PendingRegistration or NodeLifecycleState.Recovery))
            throw new InvalidLifecycleTransitionException(
                node.LifecycleState.ToString(), NodeLifecycleState.Active.ToString(),
                stateMachine.AllowedTargets(node.LifecycleState).Select(s => s.ToString()).ToArray(),
                correlationId);

        var previous = node.LifecycleState;
        var wasRecovery = previous == NodeLifecycleState.Recovery;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await bootstrapTokens.ValidateAndConsumeAsync(node.NodeId, bootstrapToken, ct))
            throw new UnauthorizedException("Invalid activation credentials", "ACTIVATION_DENIED");

        stateMachine.Validate(previous, NodeLifecycleState.Active, correlationId);
        node.LifecycleState = NodeLifecycleState.Active;
        node.RegistrationTime ??= DateTime.UtcNow;
        if (wasRecovery) node.PreviousLifecycleState = null;   // Invariant 4

        var credential = nodeSecurity.PrepareToken(node.NodeId);   // operational node token (existing model)

        await history.WriteTransitionAsync(new LifecycleTransitionRecord(
            node.NodeId, previous, NodeLifecycleState.Active, LifecycleTrigger.Activation,
            null, "system", correlationId,
            $$"""{"agentVersion":"{{agentVersion}}"}"""), ct);

        try
        {
            await auditSvc.WriteAsync(NodeManagementAuditActions.NodeActivated,
                $"node:{node.NodeId} agent:{agentVersion} corr:{correlationId}", "system", ct);
            if (wasRecovery)
                await auditSvc.WriteAsync(NodeManagementAuditActions.NodeRecoveryActivated,
                    $"node:{node.NodeId} corr:{correlationId}", "system", ct);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException($"Node {node.NodeId} was modified concurrently");
        }
        await tx.CommitAsync(ct);

        await mediator.Publish(new NodeLifecycleChangedEvent(
            node.NodeId, previous, NodeLifecycleState.Active, LifecycleTrigger.Activation, correlationId), ct);

        return new ActivateResultDto(
            credential.RawToken,
            HeartbeatIntervalSeconds: configuration.GetValue("Heartbeat:IntervalSeconds", 30),
            ProbeIntervalSeconds: configuration.GetValue("Heartbeat:ProbeIntervalSeconds", 60),
            ConfigurationVersion: 1);   // fixed until 12B-2 (spec §4.5)
    }

    // ── Operator commands ──────────────────────────────────────────────────────

    public Task EnableAsync(string nodeId, string actorUsername, CancellationToken ct = default)
        => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Active, LifecycleTrigger.Manual,
            actorUsername, null, NodeManagementAuditActions.NodeEnabled, ct: ct);

    public Task DisableAsync(string nodeId, string? reason, string actorUsername, CancellationToken ct = default)
        => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Disabled, LifecycleTrigger.Manual,
            actorUsername, reason, NodeManagementAuditActions.NodeDisabled, ct: ct);

    public async Task StartMaintenanceAsync(
        string nodeId, string reason, DateTimeOffset? expectedEndAt, bool notifyNode,
        string actorUsername, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        using var @lock = await locks.AcquireAsync(nodeId, ct);

        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");

        // Business rule (spec §4.3): maintenance settable only on Active nodes.
        if (node.LifecycleState != NodeLifecycleState.Active)
            throw new InvalidLifecycleTransitionException(
                node.LifecycleState.ToString(), "StartMaintenance",
                [NodeLifecycleState.Active.ToString()], correlationId);

        var extending = node.MaintenanceMode;   // Invariant 11: repeat = window change, audited as EXTENDED

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        node.MaintenanceMode = true;
        node.MaintenanceReason = reason;
        node.MaintenanceStartedAt ??= DateTimeOffset.UtcNow;
        node.MaintenanceUntil = expectedEndAt;
        node.MaintenanceStartedBy = actorUsername;

        // Not a lifecycle transition — history row records the maintenance event via MetadataJson
        await history.WriteTransitionAsync(new LifecycleTransitionRecord(
            nodeId, node.LifecycleState, node.LifecycleState, LifecycleTrigger.Manual,
            reason, actorUsername, correlationId,
            $$"""{"maintenance":"start","until":{{(expectedEndAt is null ? "null" : $"\"{expectedEndAt:O}\"")}},"notifyNode":{{notifyNode.ToString().ToLowerInvariant()}}}"""), ct);

        try
        {
            await auditSvc.WriteAsync(
                extending ? NodeManagementAuditActions.NodeMaintenanceExtended
                          : NodeManagementAuditActions.NodeMaintenanceStarted,
                $"node:{nodeId} reason:{reason} corr:{correlationId}", actorUsername, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyException($"Node {nodeId} was modified concurrently"); }
        await tx.CommitAsync(ct);

        await mediator.Publish(new NodeMaintenanceChangedEvent(nodeId, true), ct);
        // notifyNode: best-effort — Task 4 wires the outbound notification; nothing to do here yet.
    }

    public async Task EndMaintenanceAsync(string nodeId, string actorUsername, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        using var @lock = await locks.AcquireAsync(nodeId, ct);

        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Node {nodeId} not found", "NODE_NOT_FOUND");
        if (!node.MaintenanceMode) return;   // idempotent no-op (Invariant 11)

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        node.MaintenanceMode = false;
        node.MaintenanceReason = null;
        node.MaintenanceStartedAt = null;
        node.MaintenanceUntil = null;
        node.MaintenanceStartedBy = null;

        await history.WriteTransitionAsync(new LifecycleTransitionRecord(
            nodeId, node.LifecycleState, node.LifecycleState, LifecycleTrigger.Manual,
            null, actorUsername, correlationId, """{"maintenance":"end"}"""), ct);

        try
        {
            await auditSvc.WriteAsync(NodeManagementAuditActions.NodeMaintenanceEnded,
                $"node:{nodeId} corr:{correlationId}", actorUsername, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyException($"Node {nodeId} was modified concurrently"); }
        await tx.CommitAsync(ct);

        await mediator.Publish(new NodeMaintenanceChangedEvent(nodeId, false), ct);
    }

    // ── Decommission ───────────────────────────────────────────────────────────

    public async Task DecommissionAsync(
        string nodeId, string reason, int? gracePeriodMinutes, string actorUsername, CancellationToken ct = default)
    {
        var grace = TimeSpan.FromMinutes(gracePeriodMinutes ?? options.Value.DecommissionGraceMinutes);
        var openBatches = await NodeLifecycleHistoryService.CountOpenBatchesAsync(db, nodeId, ct);

        await ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioning, LifecycleTrigger.Manual,
            actorUsername, reason, NodeManagementAuditActions.NodeDecommissionStarted,
            mutate: async (node, _) =>
            {
                node.DecommissionReason = reason;
                node.DecommissionStartedAt = DateTimeOffset.UtcNow;
                node.DecommissionGraceUntil = DateTimeOffset.UtcNow.Add(grace);
                node.DecommissionInitialOpenBatches = openBatches;
                // Revoke trust at drain start (spec §4.7 step 3)
                await nodeSecurity.RevokeAsync(nodeId, ct);
                await bootstrapTokens.RevokeAllAsync(nodeId, ct);
            },
            metadataJson: $$"""{"graceMinutes":{{(int)grace.TotalMinutes}},"initialOpenBatches":{{openBatches}}}""",
            ct: ct);
    }

    public Task ForceCompleteDecommissionAsync(string nodeId, string actorUsername, CancellationToken ct = default)
        => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, LifecycleTrigger.Manual,
            actorUsername, "forced by operator", NodeManagementAuditActions.NodeDecommissionForced,
            mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);

    public Task FinalizeDecommissionAsync(string nodeId, LifecycleTrigger trigger, string reason, CancellationToken ct = default)
        => ExecuteTransitionAsync(nodeId, NodeLifecycleState.Decommissioned, trigger,
            "system", reason, NodeManagementAuditActions.NodeDecommissionCompleted,
            mutate: (node, _) => { FreeExternalId(node); return Task.CompletedTask; }, ct: ct);

    // ExternalId freed only when Decommissioned (spec §2.2 note). Preserve traceability in NodeName.
    private static void FreeExternalId(SyncNode node)
    {
        if (node.ExternalId.Length > 0)
        {
            node.NodeName = $"{node.NodeName} (decommissioned, was {node.ExternalId})";
            node.ExternalId = string.Empty;
        }
    }
}
