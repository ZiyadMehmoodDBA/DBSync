import { useQuery } from '@tanstack/react-query';
import { getRegistrationDetail } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useRegistrationDetail(id: number | null) {
  return useQuery({
    queryKey: nodeManagementKeys.registrationDetail(id ?? 0),
    queryFn:  ({ signal }) => getRegistrationDetail(id!, signal),
    enabled:  id !== null,
    staleTime: 60_000,
  });
}
