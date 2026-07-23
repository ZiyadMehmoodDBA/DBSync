import { useQuery } from '@tanstack/react-query';
import { clusterKeys, getClusterDiagnostics } from '../api/cluster';

export function useClusterDiagnostics() {
  return useQuery({
    queryKey:        clusterKeys.diagnostics,
    queryFn:         ({ signal }) => getClusterDiagnostics({ signal }),
    staleTime:       10_000,
    gcTime:          60_000,
    refetchInterval: 15_000,
  });
}
