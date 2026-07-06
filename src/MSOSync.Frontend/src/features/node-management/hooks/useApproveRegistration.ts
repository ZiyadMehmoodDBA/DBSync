import { useMutation } from '@tanstack/react-query';
import { approveRegistration } from '../api/nodeManagementApi';

export function useApproveRegistration() {
  return useMutation({
    mutationFn: ({ id, notes }: { id: number; notes?: string }) =>
      approveRegistration(id, notes),
  });
}
