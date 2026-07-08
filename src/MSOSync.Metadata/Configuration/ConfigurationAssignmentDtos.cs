using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed record NodeConfigurationDto(
    string NodeId,
    Guid? AssignedTemplateId,
    int? AssignedTemplateVersion,
    int? AppliedTemplateVersion,
    string? ExpectedEffectiveHash,
    string? AppliedEffectiveHash,
    ConfigurationState? ConfigurationState,
    DateTime? LastAppliedAt,
    ConfigurationSettings? EffectiveSettings,
    IReadOnlyList<NodeOverrideDto> Overrides);

public sealed record NodeOverrideDto(
    Guid Id,
    string SettingKey,
    string SettingValue,
    string OverrideSource,
    DateTime UpdatedAt);

public sealed record ConfigurationHistoryEventDto(
    Guid Id,
    string NodeId,
    string EventType,
    Guid? TemplateId,
    int? TemplateVersion,
    string? ConfigurationHash,
    string? CorrelationId,
    Guid? ActorId,
    DateTime OccurredAt,
    string? Notes);

public sealed record DriftSummaryDto(
    int NoneCount,
    int CurrentCount,
    int UpdateAvailableCount,
    int ApplyingCount,
    int DriftedCount,
    int FailedCount,
    int UnknownCount);

public sealed record DriftNodeDto(
    string NodeId,
    string NodeName,
    string? GroupId,
    Guid? AssignedTemplateId,
    string? AssignedTemplateName,
    int? AssignedTemplateVersion,
    int? AppliedTemplateVersion,
    string? ExpectedEffectiveHash,
    string? AppliedEffectiveHash,
    ConfigurationState? ConfigurationState,
    DateTime? ConfigurationStatusReportedAt);

public sealed record AssignRequest(Guid TemplateId, int Version);
public sealed record SetOverrideRequest(string Key, string Value, string Source);
public sealed record CloneTemplateRequest(string NewName);
