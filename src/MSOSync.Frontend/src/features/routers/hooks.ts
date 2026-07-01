import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getRouters } from '../../shared/api/routers';

export function useRouters() {
  return useQuery({
    queryKey: queryKeys.routers(),
    queryFn: getRouters,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}
