namespace MSOSync.Metadata.Lifecycle;

public sealed record DisableRequest(string? Reason);
public sealed record MaintenanceStartRequest(string Reason, DateTimeOffset? ExpectedEndAt, bool NotifyNode);
public sealed record DecommissionRequest(string Reason, int? GracePeriodMinutes);
public sealed record DrainRequest(string? Reason);
public sealed record ResumeDrainRequest(string? Reason);
public sealed record ActivateRequest(string ExternalId, string BootstrapToken, string AgentVersion);

public sealed record TransitionActionDto(
    string Action, bool RequiresReason, bool RequiresConfirmation, string DangerLevel);

public sealed record TransitionsDto(string CurrentState, IReadOnlyList<TransitionActionDto> AllowedTransitions);
