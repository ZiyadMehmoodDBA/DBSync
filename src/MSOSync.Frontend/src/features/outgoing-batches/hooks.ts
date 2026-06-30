import { useQuery } from '@tanstack/react-query';
import type { OutgoingBatchFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getOutgoingBatches } from '../../shared/api/batches';

export function useOutgoingBatches(filter: OutgoingBatchFilter) {
  return useQuery({
    queryKey: queryKeys.outgoingBatches(filter),
    queryFn: () => getOutgoingBatches(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
