import { useQuery } from '@tanstack/react-query';
import { clusterKeys, getRecoveryDashboard } from '../api/cluster';

export function useRecoveryDashboard() {
  return useQuery({
    queryKey:        clusterKeys.recovery,
    queryFn:         ({ signal }) => getRecoveryDashboard({ signal }),
    staleTime:       15_000,
    gcTime:          60_000,
    refetchInterval: 30_000,
  });
}
