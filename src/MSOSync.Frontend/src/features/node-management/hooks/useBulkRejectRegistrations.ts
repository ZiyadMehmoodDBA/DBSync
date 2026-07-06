import { useMutation } from '@tanstack/react-query';
import { bulkRejectRegistrations } from '../api/nodeManagementApi';

export function useBulkRejectRegistrations() {
  return useMutation({
    mutationFn: ({ ids, reason }: { ids: number[]; reason?: string }) =>
      bulkRejectRegistrations(ids, reason),
  });
}
