import { useQuery } from '@tanstack/react-query';
import { timelineKeys, getOperationTimeline } from '../api/operationTimeline';

export function useOperationTimeline(
  from:  string,
  to:    string,
  types: string[],
) {
  return useQuery({
    queryKey: timelineKeys.list(from, to, types),
    queryFn:  ({ signal }) => getOperationTimeline(from, to, types, 200, { signal }),
    enabled:  !!from && !!to,
    staleTime: 30_000,
  });
}
