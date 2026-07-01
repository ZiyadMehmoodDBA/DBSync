import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getTopologySummary, getTopologyGroups, getTopologyGraph } from '../../shared/api/topology';

export function useTopologySummary() {
  return useQuery({
    queryKey: queryKeys.topologySummary(),
    queryFn: getTopologySummary,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}

export function useTopologyGroups() {
  return useQuery({
    queryKey: queryKeys.topologyGroups(),
    queryFn: getTopologyGroups,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}

export function useTopologyGraph() {
  return useQuery({
    queryKey: queryKeys.topologyGraph(),
    queryFn: getTopologyGraph,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
