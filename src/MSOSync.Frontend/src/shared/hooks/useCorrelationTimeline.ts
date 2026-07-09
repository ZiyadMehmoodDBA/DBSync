import { useQuery } from '@tanstack/react-query';
import {
  fetchCorrelationTimeline,
  searchCorrelations,
  correlationKeys,
} from '../api/audit';

export function useCorrelationTimeline(correlationId: string) {
  return useQuery({
    queryKey: correlationKeys.timeline(correlationId),
    queryFn: () => fetchCorrelationTimeline(correlationId),
    staleTime: 30_000,
    enabled: correlationId.trim().length > 0,
  });
}

export function useCorrelationSearch(params: Record<string, string>) {
  const hasParams = Object.values(params).some((v) => v.trim().length > 0);
  return useQuery({
    queryKey: correlationKeys.search(params),
    queryFn: () => searchCorrelations(params),
    staleTime: 30_000,
    enabled: hasParams,
  });
}
