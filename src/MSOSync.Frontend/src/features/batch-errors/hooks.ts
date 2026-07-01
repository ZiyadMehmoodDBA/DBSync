import { useQuery } from '@tanstack/react-query';
import type { BatchErrorFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getBatchErrors } from '../../shared/api/batches';

export function useBatchErrors(filter: BatchErrorFilter) {
  return useQuery({
    queryKey: queryKeys.batchErrors(filter),
    queryFn: () => getBatchErrors(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}
