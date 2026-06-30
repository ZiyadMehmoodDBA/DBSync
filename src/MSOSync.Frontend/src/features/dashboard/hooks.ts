import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getDashboardSummary, getDashboardActivity } from '../../shared/api/dashboard';
import { DASHBOARD_REFRESH_MS } from '../../shared/constants/query';

export function useDashboardSummary() {
  return useQuery({
    queryKey: queryKeys.dashboardSummary(),
    queryFn: getDashboardSummary,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  });
}

export function useDashboardActivity(page: number) {
  return useQuery({
    queryKey: queryKeys.dashboardActivity(page),
    queryFn: () => getDashboardActivity(page, 20),
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
