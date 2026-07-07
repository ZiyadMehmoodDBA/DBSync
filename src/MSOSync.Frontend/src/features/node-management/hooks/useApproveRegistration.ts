import { useMutation } from '@tanstack/react-query';
import { approveRegistration } from '../api/nodeManagementApi';
import type { ApproveResultDto } from '../api/nodeManagementApi';

export type { ApproveResultDto };

export function useApproveRegistration() {
  return useMutation<ApproveResultDto, Error, { id: number; notes?: string }>({
    mutationFn: ({ id, notes }) => approveRegistration(id, notes),
  });
}
