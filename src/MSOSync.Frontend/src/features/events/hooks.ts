import { useQuery } from '@tanstack/react-query';
import type { EventFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getEvents } from '../../shared/api/events';

export function useEvents(filter: EventFilter) {
  return useQuery({
    queryKey: queryKeys.events(filter),
    queryFn: () => getEvents(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}
