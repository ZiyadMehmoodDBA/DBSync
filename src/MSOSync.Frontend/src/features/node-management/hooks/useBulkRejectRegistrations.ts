import { useMutation, useQueryClient } from '@tanstack/react-query';
import { bulkRejectRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useBulkRejectRegistrations() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ ids, reason }: { ids: number[]; reason?: string }) =>
      bulkRejectRegistrations(ids, reason),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
