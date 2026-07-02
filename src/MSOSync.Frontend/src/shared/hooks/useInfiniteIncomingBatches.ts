import { useInfiniteQuery } from '@tanstack/react-query';
import { getIncomingBatches, type CursorIncomingBatchFilter } from '../api/batches';
import { queryKeys } from '../queryKeys';

export function useInfiniteIncomingBatches(filter: CursorIncomingBatchFilter) {
  return useInfiniteQuery({
    queryKey: queryKeys.incomingBatchesInfinite(filter),
    queryFn: ({ pageParam, signal }) =>
      getIncomingBatches({ ...filter, cursor: pageParam as string | undefined }, { signal }),
    getNextPageParam: (lastPage) =>
      lastPage.hasMore ? (lastPage.nextCursor ?? undefined) : undefined,
    initialPageParam: undefined as string | undefined,
  });
}
