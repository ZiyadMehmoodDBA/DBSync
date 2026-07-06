import { useMutation, useQueryClient } from '@tanstack/react-query';
import { rejectRegistration } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useRejectRegistration() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason?: string }) =>
      rejectRegistration(id, reason),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
