namespace MSOSync.App.SignalR;

public sealed record OperationsEvent(
    OperationsEventType Type,
    string NodeId,
    string? NodeLabel,
    string? PreviousStatus,
    string? CurrentStatus,
    DateTimeOffset OccurredAt,
    string? GroupId = null);
