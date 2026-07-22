import { useQuery } from '@tanstack/react-query';
import { clusterKeys, getClusterSummary } from '../api/cluster';

export function useClusterSummary() {
  return useQuery({
    queryKey:        clusterKeys.summary,
    queryFn:         ({ signal }) => getClusterSummary({ signal }),
    staleTime:       10_000,
    gcTime:          60_000,
    refetchInterval: 15_000,
  });
}
