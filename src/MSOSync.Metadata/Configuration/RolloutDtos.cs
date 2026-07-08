namespace MSOSync.Metadata.Configuration;

public sealed record RolloutDto(
    Guid Id,
    string Status,
    Guid TemplateId,
    int TemplateVersion,
    int TargetNodeCount,
    int AppliedCount,
    int FailedCount,
    int PendingCount,
    int ProgressPercent,
    Guid InitiatedBy,
    DateTime StartedAt,
    DateTime? CompletedAt);

public sealed record StartRolloutRequest(
    Guid TemplateId,
    int TemplateVersion,
    IReadOnlyList<string> NodeIds);
