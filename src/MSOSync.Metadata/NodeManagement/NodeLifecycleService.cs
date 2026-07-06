using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed class RegistrationReceivedNotification(long registrationId) : INotification
{
    public long RegistrationId { get; } = registrationId;
}

public sealed class NodeLifecycleService(
    AppDbContext             db,
    IRegistrationDiffService diffSvc,
    IAuditService            auditSvc,
    IMediator                mediator) : INodeLifecycleService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<long> RegisterAsync(InboundRegistrationDto dto, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Derive registration type
            var existingNode = await db.Nodes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.NodeId == dto.ExternalId, ct);

            var regType = existingNode is null
                ? RegistrationType.New
                : existingNode.Status == "REGISTERED"
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

            var auditDetail = diffSummary is null
                ? $"Registration {request.RequestId} received for node {dto.ExternalId}"
                : $"Registration {request.RequestId} received for node {dto.ExternalId}. Diff: {diffSummary}";

            await auditSvc.WriteAsync("NODE_REGISTERED", auditDetail, "system", ct);

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

    public async Task ApproveAsync(
        long id, string? notes, string actorUsername, CancellationToken ct = default)
    {
        var req = await db.RegistrationRequests
            .FirstOrDefaultAsync(r => r.RequestId == id, ct)
            ?? throw new KeyNotFoundException($"Registration {id} not found.");

        if (req.Status == RegistrationStatus.Approved)
            throw new InvalidOperationException($"Registration {id} is already approved.");

        req.Status      = RegistrationStatus.Approved;
        req.ProcessedAt = DateTime.UtcNow;
        req.ProcessedBy = actorUsername;
        req.Approved    = true;

        await db.SaveChangesAsync(ct);

        var auditAction = req.RegistrationType == RegistrationType.ReRegistration
            ? "NODE_RE_REGISTERED"
            : "NODE_APPROVED";

        await auditSvc.WriteAsync(auditAction,
            $"Registration {id} approved by {actorUsername}. Notes: {notes}",
            actorUsername, ct);

        NodeManagementMetrics.ApprovalsTotal.Add(1);
    }

    public async Task RejectAsync(
        long id, string? reason, string actorUsername, CancellationToken ct = default)
    {
        var req = await db.RegistrationRequests
            .FirstOrDefaultAsync(r => r.RequestId == id, ct)
            ?? throw new KeyNotFoundException($"Registration {id} not found.");

        req.Status      = RegistrationStatus.Rejected;
        req.ProcessedAt = DateTime.UtcNow;
        req.ProcessedBy = actorUsername;

        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync("NODE_REJECTED",
            $"Registration {id} rejected by {actorUsername}. Reason: {reason}",
            actorUsername, ct);

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

                    req.Status      = RegistrationStatus.Approved;
                    req.ProcessedAt = DateTime.UtcNow;
                    req.ProcessedBy = actorUsername;
                    req.Approved    = true;
                    await db.SaveChangesAsync(ct);

                    await auditSvc.WriteAsync("NODE_APPROVED",
                        $"Bulk: Registration {id} approved by {actorUsername}", actorUsername, ct);

                    NodeManagementMetrics.ApprovalsTotal.Add(1);
                    results.Add(new BulkResultItemDto(id, "Approved"));
                }
                catch (DbUpdateConcurrencyException)
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
                var req = await db.RegistrationRequests
                    .FirstOrDefaultAsync(r => r.RequestId == id, ct);
                if (req is null) { results.Add(new BulkResultItemDto(id, "NotFound")); continue; }
                if (req.Status == RegistrationStatus.Rejected)
                { results.Add(new BulkResultItemDto(id, "AlreadyRejected")); continue; }

                req.Status      = RegistrationStatus.Rejected;
                req.ProcessedAt = DateTime.UtcNow;
                req.ProcessedBy = actorUsername;
                await db.SaveChangesAsync(ct);

                await auditSvc.WriteAsync("NODE_REJECTED",
                    $"Bulk: Registration {id} rejected by {actorUsername}. Reason: {reason}",
                    actorUsername, ct);

                NodeManagementMetrics.RejectionsTotal.Add(1);
                results.Add(new BulkResultItemDto(id, "Rejected"));
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
        // Generate cryptographically random 32-byte base64url token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token      = Convert.ToBase64String(tokenBytes)
                                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var nodeId = dto.ExternalId;

        // Create SyncNode
        var node = new SyncNode
        {
            NodeId   = nodeId,
            GroupId  = dto.GroupId ?? "default",
            SyncUrl  = $"https://{dto.NodeName}.local:8080",
            Status   = "PROVISIONED",
            DbServer = dto.DbServer,
            DbName   = dto.DbName,
        };

        db.Nodes.Add(node);
        await db.SaveChangesAsync(ct);

        // Audit: write "token:issued" — never the token value
        await auditSvc.WriteAsync("NODE_PROVISIONED",
            $"Node {nodeId} provisioned by {actorUsername}. token:issued",
            actorUsername, ct);

        return new ProvisionResultDto(nodeId, token);
    }
}
