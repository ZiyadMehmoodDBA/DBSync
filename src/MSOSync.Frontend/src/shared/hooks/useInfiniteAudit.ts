import { useInfiniteQuery } from '@tanstack/react-query';
import { getAuditLog, type CursorAuditFilter } from '../api/audit';
import { queryKeys } from '../queryKeys';

export function useInfiniteAudit(filter: CursorAuditFilter) {
  return useInfiniteQuery({
    queryKey: queryKeys.auditLogInfinite(filter),
    queryFn: ({ pageParam, signal }) =>
      getAuditLog({ ...filter, cursor: pageParam as string | undefined }, { signal }),
    getNextPageParam: (lastPage) =>
      lastPage.hasMore ? (lastPage.nextCursor ?? undefined) : undefined,
    initialPageParam: undefined as string | undefined,
  });
}
