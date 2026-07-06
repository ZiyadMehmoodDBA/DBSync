import { useQuery } from '@tanstack/react-query';
import { getRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';
import type { RegistrationListFilter } from '../types/registration';

export function useNodeManagementRegistrations(filter: RegistrationListFilter) {
  return useQuery({
    queryKey: nodeManagementKeys.registrations(filter),
    queryFn:  ({ signal }) => getRegistrations(filter, signal),
    staleTime: 15_000,
  });
}
