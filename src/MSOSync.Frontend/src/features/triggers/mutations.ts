import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  enableTrigger,
  disableTrigger,
  rebuildTrigger,
  verifyTrigger,
  createTrigger,
  updateTrigger,
  deleteTrigger,
} from '../../shared/api/triggers';
import type { CreateTriggerRequest, UpdateTriggerRequest } from '../../shared/api/triggers';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

export function useEnableTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => enableTrigger(triggerId),
    onSuccess: () => {
      toast.success('Trigger enabled');
      invalidateTriggerRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useDisableTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => disableTrigger(triggerId),
    onSuccess: () => {
      toast.success('Trigger disabled');
      invalidateTriggerRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useRebuildTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => rebuildTrigger(triggerId),
    onSuccess: () => {
      toast.success('Trigger rebuilt');
      invalidateTriggerRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useVerifyTriggerMutation() {
  return useMutation({
    mutationFn: (triggerId: string) => verifyTrigger(triggerId),
    onSuccess: () => {
      toast.info('Trigger verified successfully.');
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

function invalidateTriggerRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useCreateTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateTriggerRequest) => createTrigger(data),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ triggerId, data }: { triggerId: string; data: UpdateTriggerRequest }) =>
      updateTrigger(triggerId, data),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeleteTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => deleteTrigger(triggerId),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
    // no onError — caller handles it
  });
}
