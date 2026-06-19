using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record ParameterChangedEvent(
    string ParameterName,
    string? OldValue,
    string? NewValue) : INotification;
