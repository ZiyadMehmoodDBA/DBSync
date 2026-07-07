using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record NodeMaintenanceChangedEvent(string NodeId, bool Enabled) : INotification;
