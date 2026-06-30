import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  enableTrigger,
  disableTrigger,
  rebuildTrigger,
  verifyTrigger,
} from '../../shared/api/triggers';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

export function useEnableTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => enableTrigger(triggerId),
    onSuccess: () => {
      toast.success('Trigger enabled');
      void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
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
      void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
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
      void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
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
