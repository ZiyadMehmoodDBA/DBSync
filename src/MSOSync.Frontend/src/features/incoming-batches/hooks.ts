import { useQuery } from '@tanstack/react-query';
import type { IncomingBatchFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getIncomingBatches } from '../../shared/api/batches';

export function useIncomingBatches(filter: IncomingBatchFilter) {
  return useQuery({
    queryKey: queryKeys.incomingBatches(filter),
    queryFn: () => getIncomingBatches(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}
