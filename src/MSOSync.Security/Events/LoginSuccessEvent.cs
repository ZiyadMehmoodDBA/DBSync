using MediatR;

namespace MSOSync.Security.Events;

public sealed record LoginSuccessEvent(string Username, string CorrelationId) : INotification;
