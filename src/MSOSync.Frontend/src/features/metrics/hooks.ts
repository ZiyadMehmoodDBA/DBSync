import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import {
  getMetricsSummary,
  getNodeMetrics,
  getChannelMetrics,
  getRuntimeMetrics,
} from '../../shared/api/metrics';
import { DASHBOARD_REFRESH_MS } from '../../shared/constants/query';

export function useMetricsSummary() {
  return useQuery({
    queryKey: queryKeys.metricsSummary(),
    queryFn: getMetricsSummary,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 60_000,
  });
}

export function useRuntimeMetrics() {
  return useQuery({
    queryKey: queryKeys.runtimeMetrics(),
    queryFn: getRuntimeMetrics,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 60_000,
  });
}

export function useNodeMetrics() {
  return useQuery({
    queryKey: queryKeys.nodeMetrics(),
    queryFn: getNodeMetrics,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}

export function useChannelMetrics() {
  return useQuery({
    queryKey: queryKeys.channelMetrics(),
    queryFn: getChannelMetrics,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
