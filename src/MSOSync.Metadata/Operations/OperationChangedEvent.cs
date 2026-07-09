using MediatR;

namespace MSOSync.Metadata.Operations;

/// <summary>
/// Published by OperationService after every state change to a sync_operation row.
/// Handled by OperationChangedPublisher (in MSOSync.App) which fans the event out over SignalR.
/// </summary>
public sealed record OperationChangedEvent(
    Guid   OperationId,
    string OperationType,
    string Status) : INotification;
