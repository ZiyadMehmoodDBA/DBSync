import { useMutation } from '@tanstack/react-query';
import { provision } from '../api/nodeManagementApi';
import type { ProvisionRequest, ProvisionResult } from '../types/provision';

export function useProvision() {
  return useMutation<ProvisionResult, Error, ProvisionRequest>({
    mutationFn: provision,
  });
}
