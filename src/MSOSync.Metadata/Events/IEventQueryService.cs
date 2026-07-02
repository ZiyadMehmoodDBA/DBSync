using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.Events;

public interface IEventQueryService
{
    Task<CursorPageResult<EventSummaryDto>> GetEventsAsync(
        EventFilter filter, CancellationToken ct = default);

    Task<EventDetailDto?> GetEventByIdAsync(
        long eventId, CancellationToken ct = default);
}
