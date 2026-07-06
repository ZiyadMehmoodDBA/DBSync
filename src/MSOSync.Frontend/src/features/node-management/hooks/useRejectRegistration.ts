import { useMutation } from '@tanstack/react-query';
import { rejectRegistration } from '../api/nodeManagementApi';

export function useRejectRegistration() {
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason?: string }) =>
      rejectRegistration(id, reason),
  });
}
