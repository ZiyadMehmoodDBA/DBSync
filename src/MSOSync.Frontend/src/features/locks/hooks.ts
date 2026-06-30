import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getLocks } from '../../shared/api/locks';

export function useLocks() {
  return useQuery({
    queryKey: queryKeys.locks(),
    queryFn: getLocks,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
