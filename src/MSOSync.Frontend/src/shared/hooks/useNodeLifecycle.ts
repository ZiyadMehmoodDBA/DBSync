import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  decommissionNode, disableNode, drainNode, enableNode, endMaintenance, forceCompleteDecommission,
  getNodeLifecycleHistory, getNodeState, getNodeTransitions, resumeDrain, startMaintenance,
} from '../api/lifecycle';
import type { LifecycleHistoryFilter } from '../types/lifecycle';
import { getErrorMessage } from '../utils/error';
import { queryKeys } from '../queryKeys';

export function useNodeState(nodeId: string) {
  return useQuery({
    queryKey: queryKeys.nodeState(nodeId),
    queryFn: ({ signal }) => getNodeState(nodeId, { signal }),
  });
}

export function useNodeTransitions(nodeId: string) {
  return useQuery({
    queryKey: queryKeys.nodeTransitions(nodeId),
    queryFn: ({ signal }) => getNodeTransitions(nodeId, { signal }),
  });
}

export function useNodeLifecycleHistory(nodeId: string, filter: LifecycleHistoryFilter = {}) {
  return useQuery({
    queryKey: queryKeys.nodeLifecycleHistory(nodeId, filter),
    queryFn: ({ signal }) => getNodeLifecycleHistory(nodeId, filter, { signal }),
  });
}

export function invalidateLifecycle(qc: QueryClient, nodeId: string) {
  void qc.invalidateQueries({ queryKey: ['nodes'] });
  void qc.invalidateQueries({ queryKey: queryKeys.nodeState(nodeId) });
  void qc.invalidateQueries({ queryKey: queryKeys.nodeTransitions(nodeId) });
  void qc.invalidateQueries({ queryKey: ['node-lifecycle-history', nodeId] });
  void qc.invalidateQueries({ queryKey: ['node-management', 'overview'] });
  void qc.invalidateQueries({ queryKey: queryKeys.topologyGraph() });
  void qc.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
  void qc.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
}

function lifecycleMutation<TArgs>(
  fn: (args: TArgs) => Promise<void>,
  successMessage: string,
  nodeIdOf: (args: TArgs) => string,
) {
  // Factory keeps the 10C/10D toast-on-settled pattern in ONE place.
  return function useLifecycleMutation() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: fn,
      onSuccess: (_data, args) => {
        toast.success(successMessage);
        invalidateLifecycle(qc, nodeIdOf(args));
      },
      onError: (error) => toast.error(getErrorMessage(error)),
    });
  };
}

export const useEnableNode = lifecycleMutation(
  (a: { nodeId: string }) => enableNode(a.nodeId), 'Node enabled', (a) => a.nodeId);

export const useDisableNode = lifecycleMutation(
  (a: { nodeId: string; reason?: string }) => disableNode(a.nodeId, a.reason),
  'Node disabled', (a) => a.nodeId);

export const useStartMaintenance = lifecycleMutation(
  (a: { nodeId: string; reason: string; expectedEndAt?: string; notifyNode: boolean }) =>
    startMaintenance(a.nodeId, { reason: a.reason, expectedEndAt: a.expectedEndAt, notifyNode: a.notifyNode }),
  'Maintenance started', (a) => a.nodeId);

export const useEndMaintenance = lifecycleMutation(
  (a: { nodeId: string }) => endMaintenance(a.nodeId), 'Maintenance ended', (a) => a.nodeId);

export const useDecommissionNode = lifecycleMutation(
  (a: { nodeId: string; reason: string; gracePeriodMinutes?: number }) =>
    decommissionNode(a.nodeId, { reason: a.reason, gracePeriodMinutes: a.gracePeriodMinutes }),
  'Decommission started', (a) => a.nodeId);

export const useForceCompleteDecommission = lifecycleMutation(
  (a: { nodeId: string }) => forceCompleteDecommission(a.nodeId),
  'Decommission completed', (a) => a.nodeId);

export const useStartDrain = lifecycleMutation(
  (a: { nodeId: string; reason?: string }) => drainNode(a.nodeId, a.reason),
  'Drain started', (a) => a.nodeId);

export const useResumeDrain = lifecycleMutation(
  (a: { nodeId: string; reason?: string }) => resumeDrain(a.nodeId, a.reason),
  'Drain resumed', (a) => a.nodeId);
