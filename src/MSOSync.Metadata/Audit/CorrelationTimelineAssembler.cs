using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Metadata.Audit;

public sealed class CorrelationTimelineAssembler(
    AppDbContext                   db,
    IPlatformRepository<SyncAudit> auditRepo)
{
    // Phase name assignment — more specific prefixes checked first
    private static readonly Dictionary<string, string> PhaseMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NODE_REGISTERED"]              = "Registration",
        ["NODE_REGISTRATION_REQUESTED"]  = "Registration",
        ["NODE_APPROVED"]                = "Lifecycle",
        ["NODE_REJECTED"]                = "Lifecycle",
        ["NODE_DECOMMISSION_INITIATED"]  = "Lifecycle",
        ["NODE_ACTIVATED"]               = "Lifecycle",
        ["NODE_DISABLED"]                = "Lifecycle",
        ["ROLLOUT_STARTED"]              = "Configuration",
        ["ROLLOUT_COMPLETED"]            = "Configuration",
        ["ROLLOUT_FAILED"]               = "Configuration",
        ["CONFIGURATION_APPLIED"]        = "Configuration",
        ["CONFIGURATION_OVERRIDDEN"]     = "Configuration",
        ["EXPORT_JOB_CREATED"]           = "Operation",
        ["EXPORT_JOB_COMPLETED"]         = "Operation",
        ["EXPORT_JOB_FAILED"]            = "Operation",
        ["PARAMETER_UPDATED"]            = "System",
        ["HEARTBEAT_RECEIVED"]           = "System",
        ["AUTH_LOGIN"]                   = "Security",
        ["AUTH_FAILED"]                  = "Security",
        ["TOKEN_REVOKED"]                = "Security",
        ["TOKEN_ISSUED"]                 = "Security",
    };

    private static readonly string[] PhaseOrder =
        ["Registration", "Lifecycle", "Configuration", "Operation", "Security", "System"];

    public async Task<CorrelationTimelineDto?> AssembleAsync(
        string correlationId, CancellationToken ct)
    {
        var auditRows = await auditRepo.QueryAll()
            .Where(a => a.CorrelationId == correlationId)
            .OrderBy(a => a.CreateTime)
            .ToListAsync(ct);

        if (auditRows.Count == 0)
            return null;

        // Load the associated operation if any
        var operation = await db.Operations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.CorrelationId == correlationId, ct);

        // Map audit rows to CorrelationEventDto
        DateTime? prevTime = null;
        var events = auditRows.Select(a =>
        {
            var occurredAt = a.CreateTime ?? DateTime.UtcNow;
            var durationSince = prevTime.HasValue
                ? occurredAt - prevTime.Value
                : (TimeSpan?)null;
            prevTime = occurredAt;

            var actionName = a.ActionName ?? "";
            var category   = DeriveCategory(actionName);
            var severity   = DeriveSeverity(actionName);
            var deepLink   = DeriveDeepLink(actionName, null, operation?.OperationId);

            return new CorrelationEventDto(
                AuditId:              a.AuditId,
                OccurredAt:           occurredAt,
                DurationSincePrevious: durationSince,
                ActionName:           actionName,
                Summary:              a.ObjectName ?? actionName,
                ActorUsername:        a.Username,
                Category:             category,
                Severity:             severity,
                EntityType:           null,
                EntityId:             null,
                DeepLink:             deepLink);
        }).ToArray();

        // Group into phases, in defined order
        var grouped = events
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).ToArray());

        var phases = PhaseOrder
            .Where(p => grouped.ContainsKey(p))
            .Select(p =>
            {
                var phaseEvents = grouped[p];
                var hasErrors   = phaseEvents.Any(e => e.Severity is "Error" or "Critical");
                return new CorrelationPhaseDto(p, p, phaseEvents, hasErrors);
            })
            .ToArray();

        // Include any categories not in PhaseOrder
        var knownPhases = new HashSet<string>(PhaseOrder);
        var extraPhases = grouped.Keys
            .Where(k => !knownPhases.Contains(k))
            .Select(k =>
            {
                var phaseEvents = grouped[k];
                var hasErrors   = phaseEvents.Any(e => e.Severity is "Error" or "Critical");
                return new CorrelationPhaseDto(k, k, phaseEvents, hasErrors);
            })
            .ToArray();
        if (extraPhases.Length > 0)
            phases = [.. phases, .. extraPhases];

        // Entity chips: empty since SyncAudit has no EntityType/EntityId columns
        var chips = Array.Empty<EntityChipDto>();

        // Determine failed workflow: any event has Error/Critical severity
        var isFailedWorkflow = events.Any(e => e.Severity is "Error" or "Critical")
            || operation?.Result is "Failure" or "Cancelled";

        var failedEvent     = events.FirstOrDefault(e => e.Severity is "Error" or "Critical");
        string? failureSummary = isFailedWorkflow ? (failedEvent?.Summary ?? events[^1].Summary) : null;

        var firstOccurred = events[0].OccurredAt;
        var lastOccurred  = events[^1].OccurredAt;
        var duration      = lastOccurred - firstOccurred;

        return new CorrelationTimelineDto(
            CorrelationId:   correlationId,
            OperationId:     operation?.OperationId,
            OperationType:   operation?.OperationType,
            OperationStatus: operation?.Status,
            OperationResult: operation?.Result,
            StartedAt:       firstOccurred,
            CompletedAt:     lastOccurred,
            Duration:        duration,
            InitiatedBy:     events.FirstOrDefault(e => e.ActorUsername is not null)?.ActorUsername,
            EntityChips:     chips,
            TotalEventCount: events.Length,
            IsFailedWorkflow: isFailedWorkflow,
            FailureSummary:  failureSummary,
            Phases:          phases);
    }

    public async Task<CorrelationSearchResultDto[]> SearchAsync(
        string? q,
        DateTime? from,
        DateTime? to,
        CancellationToken ct)
    {
        var query = auditRepo.QueryAll()
            .Where(a => a.CorrelationId != null);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a =>
                a.CorrelationId!.StartsWith(q)
                || (a.ActionName != null && a.ActionName.Contains(q))
                || (a.Username   != null && a.Username.Contains(q)));

        if (from.HasValue)
            query = query.Where(a => a.CreateTime >= from);
        if (to.HasValue)
            query = query.Where(a => a.CreateTime <= to);

        // Group by CorrelationId, collect aggregate stats
        var grouped = await query
            .GroupBy(a => a.CorrelationId!)
            .Select(g => new
            {
                CorrelationId = g.Key,
                EventCount    = g.Count(),
                FirstSeen     = g.Min(a => a.CreateTime),
                LastSeen      = g.Max(a => a.CreateTime),
                // Detect failed workflow: look for FAILED/ERROR/REJECTED in action names
                HasFailedAction = g.Any(a => a.ActionName != null && (
                    a.ActionName.Contains("FAILED") ||
                    a.ActionName.Contains("ERROR")  ||
                    a.ActionName.Contains("REJECTED")))
            })
            .OrderByDescending(x => x.LastSeen)
            .Take(50)
            .ToListAsync(ct);

        return grouped
            .Select(r => new CorrelationSearchResultDto(
                CorrelationId:     r.CorrelationId,
                EventCount:        r.EventCount,
                FirstSeen:         r.FirstSeen ?? DateTime.UtcNow,
                LastSeen:          r.LastSeen  ?? DateTime.UtcNow,
                PrimaryEntityType: null,
                IsFailedWorkflow:  r.HasFailedAction))
            .ToArray();
    }

    private static string DeriveCategory(string actionName)
    {
        if (PhaseMap.TryGetValue(actionName, out var exact))
            return exact;

        // Prefix-based fallback
        if (actionName.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase))           return "Lifecycle";
        if (actionName.StartsWith("BOOTSTRAP_", StringComparison.OrdinalIgnoreCase))      return "Lifecycle";
        if (actionName.StartsWith("CONFIGURATION_", StringComparison.OrdinalIgnoreCase))  return "Configuration";
        if (actionName.StartsWith("ROLLOUT_", StringComparison.OrdinalIgnoreCase))        return "Configuration";
        if (actionName.StartsWith("EXPORT_", StringComparison.OrdinalIgnoreCase))         return "Operation";
        if (actionName.StartsWith("AUTH_", StringComparison.OrdinalIgnoreCase))           return "Security";
        if (actionName.StartsWith("TOKEN_", StringComparison.OrdinalIgnoreCase))          return "Security";

        return "System";
    }

    private static string DeriveSeverity(string actionName)
    {
        if (actionName.Contains("FAILED", StringComparison.OrdinalIgnoreCase)  ||
            actionName.Contains("ERROR",  StringComparison.OrdinalIgnoreCase))
            return "Error";
        if (actionName.Contains("REJECTED", StringComparison.OrdinalIgnoreCase) ||
            actionName.Contains("DENIED",   StringComparison.OrdinalIgnoreCase))
            return "Warning";
        if (actionName.Contains("DELETED", StringComparison.OrdinalIgnoreCase) ||
            actionName.Contains("REVOKED", StringComparison.OrdinalIgnoreCase))
            return "Warning";
        return "Info";
    }

    private static string? DeriveDeepLink(string actionName, string? entityId, Guid? operationId)
        => actionName switch
        {
            var a when a.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase) && entityId is not null
                => $"/operations/nodes/{entityId}",
            var a when a.StartsWith("ROLLOUT_", StringComparison.OrdinalIgnoreCase) && operationId.HasValue
                => $"/operations/jobs/{operationId}",
            var a when a.StartsWith("CONFIGURATION_", StringComparison.OrdinalIgnoreCase) && entityId is not null
                => $"/configuration/templates/{entityId}",
            var a when a.StartsWith("EXPORT_", StringComparison.OrdinalIgnoreCase) && operationId.HasValue
                => $"/operations/jobs/{operationId}",
            _ => null
        };
}
