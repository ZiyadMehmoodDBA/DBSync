import { useQuery } from '@tanstack/react-query';
import { getOverview } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useNodeManagementOverview() {
  return useQuery({
    queryKey: nodeManagementKeys.overview(),
    queryFn:  getOverview,
    staleTime: 30_000,
  });
}
