import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { enableNode, disableNode, approveRegistration, updateNode } from '../../shared/api/nodes';
import type { UpdateNodeRequest } from '../../shared/api/nodes';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

function invalidateNodeRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.nodes() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.metricsSummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useEnableNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (nodeId: string) => enableNode(nodeId),
    onSuccess: () => {
      toast.success('Node enabled');
      invalidateNodeRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useDisableNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (nodeId: string) => disableNode(nodeId),
    onSuccess: () => {
      toast.success('Node disabled');
      invalidateNodeRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useApproveRegistrationMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (requestId: string) => approveRegistration(requestId),
    onSuccess: () => {
      toast.success('Registration approved');
      invalidateNodeRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
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
