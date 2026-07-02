import { useInfiniteQuery } from '@tanstack/react-query';
import { getEvents, type CursorEventFilter } from '../api/events';
import { queryKeys } from '../queryKeys';

export function useInfiniteEvents(filter: CursorEventFilter) {
  return useInfiniteQuery({
    queryKey: queryKeys.eventsInfinite(filter),
    queryFn: ({ pageParam, signal }) =>
      getEvents({ ...filter, cursor: pageParam as string | undefined }, { signal }),
    getNextPageParam: (lastPage) =>
      lastPage.hasMore ? (lastPage.nextCursor ?? undefined) : undefined,
    initialPageParam: undefined as string | undefined,
  });
}
