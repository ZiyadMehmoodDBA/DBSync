import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getTriggers } from '../../shared/api/triggers';

export function useTriggers() {
  return useQuery({
    queryKey: queryKeys.triggers(),
    queryFn: getTriggers,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}
