import { useInfiniteQuery } from '@tanstack/react-query';
import { getRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';
import type { RegistrationFilter } from '../types/registration';

export function useNodeManagementRegistrations(filter: RegistrationFilter) {
  return useInfiniteQuery({
    queryKey: nodeManagementKeys.registrations(filter),
    queryFn: ({ pageParam }) =>
      getRegistrations({ ...filter, cursor: pageParam as string | undefined }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  });
}
