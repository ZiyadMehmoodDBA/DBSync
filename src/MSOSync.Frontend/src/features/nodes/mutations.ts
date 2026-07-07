import { useMutation, useQueryClient } from '@tanstack/react-query';
import { updateNode, createNode } from '../../shared/api/nodes';
import type { UpdateNodeRequest, CreateNodeRequest } from '../../shared/api/nodes';
import { queryKeys } from '../../shared/queryKeys';

function invalidateNodeRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: ['nodes'] });
  void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.metricsSummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGraph() });
}

export function useUpdateNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ nodeId, data }: { nodeId: string; data: UpdateNodeRequest }) =>
      updateNode(nodeId, data),
    onSuccess: () => {
      invalidateNodeRelated(queryClient);
    },
    // no onError — caller handles it
  });
}

export function useCreateNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateNodeRequest) => createNode(data),
    onSuccess: () => {
      invalidateNodeRelated(queryClient);
    },
    // no toast here — caller shows one-time token banner
  });
}
