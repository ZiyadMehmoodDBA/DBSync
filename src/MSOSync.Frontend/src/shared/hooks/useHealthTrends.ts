import { useQuery } from '@tanstack/react-query';
import { clusterKeys, getHealthTrends } from '../api/cluster';

export function useHealthTrends(window: string, nodeId?: string) {
  return useQuery({
    queryKey:  clusterKeys.healthTrends(window, nodeId),
    queryFn:   ({ signal }) => getHealthTrends(window, nodeId, { signal }),
    staleTime: 30_000,
    gcTime:    120_000,
  });
}
