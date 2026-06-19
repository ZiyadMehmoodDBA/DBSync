using MediatR;

namespace MSOSync.Security.Events;

public sealed record TokenReuseDetectedEvent(string Username, long FamilyId, string CorrelationId) : INotification;
