import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  abortRollingOperation, confirmRollingStep, createRollingOperation,
  getRollingOperation, pauseRollingOperation, resumeRollingOperation, rollingKeys,
} from '../api/rolling';
import { operationKeys } from '../api/operations';
import type { CreateRollingOperationRequest } from '../types/rolling';
import { getErrorMessage } from '../utils/error';

export function useRollingOperation(id: string | null) {
  return useQuery({
    queryKey: rollingKeys.detail(id ?? ''),
    queryFn: ({ signal }) => getRollingOperation(id!, { signal }),
    enabled: id !== null,
    refetchInterval: 5_000,
  });
}

function rollingMutation<TArgs>(fn: (args: TArgs) => Promise<unknown>, successMessage: string) {
  return function useRollingMutation() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: fn,
      onSuccess: () => {
        toast.success(successMessage);
        void qc.invalidateQueries({ queryKey: rollingKeys.all });
        void qc.invalidateQueries({ queryKey: operationKeys.all });
      },
      onError: (error) => toast.error(getErrorMessage(error)),
    });
  };
}

export const useCreateRollingOperation = rollingMutation(
  (body: CreateRollingOperationRequest) => createRollingOperation(body), 'Rolling operation created');
export const usePauseRollingOperation  = rollingMutation(pauseRollingOperation,  'Operation paused');
export const useResumeRollingOperation = rollingMutation(resumeRollingOperation, 'Operation resumed');
export const useAbortRollingOperation  = rollingMutation(abortRollingOperation,  'Operation aborted');
export const useConfirmRollingStep     = rollingMutation(confirmRollingStep,     'Step confirmed');
