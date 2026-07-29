import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getHealthScores, getSloStatus } from '../../shared/api/observability';
import { DASHBOARD_REFRESH_MS } from '../../shared/constants/query';

export function useHealthScores() {
  return useQuery({
    queryKey: queryKeys.healthScores(),
    queryFn: getHealthScores,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 20_000,
  });
}

export function useSloStatus() {
  return useQuery({
    queryKey: queryKeys.sloStatus(),
    queryFn: getSloStatus,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 60_000,
  });
}
