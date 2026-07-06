import { useInfiniteQuery } from '@tanstack/react-query';
import { getRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';
import type { RegistrationFilter } from '../types/registration';

export function useNodeManagementRegistrations(filter: RegistrationFilter) {
  return useInfiniteQuery({
    queryKey: nodeManagementKeys.registrations(filter),
    queryFn: ({ pageParam, signal }) =>
      getRegistrations({ ...filter, cursor: pageParam as string | undefined }, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  });
}
