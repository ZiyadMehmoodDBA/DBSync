import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../shared/queryKeys';
import { getNodeScope, setNodeScope } from '../../../shared/api/nodeScope';
import type { SetNodeScopeRequest } from '../../../shared/types';

export function useNodeScope(nodeId: string) {
  return useQuery({
    queryKey: queryKeys.nodeScope(nodeId),
    queryFn: () => getNodeScope(nodeId),
    staleTime: 30_000,
    retry: false,
  });
}

export function useSetNodeScope(nodeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: SetNodeScopeRequest) => setNodeScope(nodeId, req),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: queryKeys.nodeScope(nodeId) });
    },
  });
}
