import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getAuditLog, type CursorAuditFilter } from '../../shared/api/audit';

export function useAuditLog(filter: CursorAuditFilter) {
  return useQuery({
    queryKey: queryKeys.auditLogInfinite(filter),
    queryFn: ({ signal }) => getAuditLog(filter, { signal }),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
