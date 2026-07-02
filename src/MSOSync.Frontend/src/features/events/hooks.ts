import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getEvents, type CursorEventFilter } from '../../shared/api/events';

export function useEvents(filter: CursorEventFilter) {
  return useQuery({
    queryKey: queryKeys.eventsInfinite(filter),
    queryFn: ({ signal }) => getEvents(filter, { signal }),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}
