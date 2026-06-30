import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getNodes } from '../../shared/api/nodes';

export function useNodes() {
  return useQuery({
    queryKey: queryKeys.nodes(),
    queryFn: getNodes,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
