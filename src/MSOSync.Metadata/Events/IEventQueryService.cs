using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.Events;

public interface IEventQueryService
{
    Task<PagedResult<EventSummaryDto>> GetEventsAsync(
        EventFilter filter, CancellationToken ct = default);

    Task<EventDetailDto?> GetEventByIdAsync(
        long eventId, CancellationToken ct = default);
}
