using MediatR;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Events;

public sealed record ConfigurationChangedEvent(
    string              NodeId,
    Guid?               TemplateId,
    int?                AssignedVersion,
    ConfigurationState? ConfigurationState,
    string?             CorrelationId) : INotification;
