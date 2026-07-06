import { useMutation } from '@tanstack/react-query';
import { bulkApproveRegistrations } from '../api/nodeManagementApi';

export function useBulkApproveRegistrations() {
  return useMutation({
    mutationFn: ({ ids }: { ids: number[] }) => bulkApproveRegistrations(ids),
  });
}
