import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getIncomingBatches, type CursorIncomingBatchFilter } from '../../shared/api/batches';

export function useIncomingBatches(filter: CursorIncomingBatchFilter) {
  return useQuery({
    queryKey: queryKeys.incomingBatchesInfinite(filter),
    queryFn: ({ signal }) => getIncomingBatches(filter, { signal }),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}
