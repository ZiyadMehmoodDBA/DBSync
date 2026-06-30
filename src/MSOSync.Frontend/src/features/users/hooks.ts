import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getUsers } from '../../shared/api/users';

const ALL_USERS_FILTER = { page: 1, pageSize: 200 } as const;

export function useUsers() {
  return useQuery({
    queryKey: queryKeys.users(ALL_USERS_FILTER),
    queryFn: () => getUsers(ALL_USERS_FILTER),
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
