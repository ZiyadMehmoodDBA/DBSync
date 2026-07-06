import { useMutation, useQueryClient } from '@tanstack/react-query';
import { approveRegistration } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useApproveRegistration() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, notes }: { id: number; notes?: string }) =>
      approveRegistration(id, notes),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
