using MediatR;

namespace MSOSync.Security.Events;

public sealed record AccountLockedEvent(string Username, string CorrelationId) : INotification;
