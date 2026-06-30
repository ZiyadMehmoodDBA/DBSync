import { useQuery } from '@tanstack/react-query';
import type { AuditFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getAuditLog } from '../../shared/api/audit';

export function useAuditLog(filter: AuditFilter) {
  return useQuery({
    queryKey: queryKeys.auditLog(filter),
    queryFn: () => getAuditLog(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
