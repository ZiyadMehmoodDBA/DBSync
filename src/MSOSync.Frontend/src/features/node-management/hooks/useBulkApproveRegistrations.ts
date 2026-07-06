import { useMutation, useQueryClient } from '@tanstack/react-query';
import { bulkApproveRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useBulkApproveRegistrations() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ ids }: { ids: number[] }) => bulkApproveRegistrations(ids),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
