import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getNodes } from '../../shared/api/nodes';

export function useNodes(pageNumber = 1, pageSize = 50) {
  return useQuery({
    queryKey: queryKeys.nodes(pageNumber, pageSize),
    queryFn: ({ signal }) => getNodes(pageNumber, pageSize, { signal }),
    staleTime: 10_000,
  });
}
