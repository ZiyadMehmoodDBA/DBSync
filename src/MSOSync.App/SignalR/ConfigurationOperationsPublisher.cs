using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Events;

namespace MSOSync.App.SignalR;

public sealed class ConfigurationOperationsPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<ConfigurationChangedEvent>
{
    public async Task Handle(ConfigurationChangedEvent evt, CancellationToken ct)
    {
        var payload = new OperationsEvent(
            Type:           OperationsEventType.ConfigurationChanged,
            NodeId:         evt.NodeId,
            NodeLabel:      null,
            PreviousStatus: null,
            CurrentStatus:  evt.ConfigurationState?.ToString(),
            OccurredAt:     DateTimeOffset.UtcNow)
        {
            CorrelationId = evt.CorrelationId is null ? null : Guid.TryParse(evt.CorrelationId, out var g) ? g : null,
        };

        await hub.Clients.Group("operators").SendAsync("OperationsEvent", payload, ct);
        await hub.Clients.Group($"node-{evt.NodeId}").SendAsync("OperationsEvent", payload, ct);
    }
}
