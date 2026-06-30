import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { retryBatch, retryAllBatches } from '../../shared/api/batches';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

export function useRetryBatchMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (batchId: number) => retryBatch(batchId),
    onSuccess: () => {
      toast.success('Batch queued for retry');
      void queryClient.invalidateQueries({ queryKey: ['outgoing-batches'] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useRetryAllBatchesMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => retryAllBatches(),
    onSuccess: () => {
      toast.success('All failed batches queued for retry');
      void queryClient.invalidateQueries({ queryKey: ['outgoing-batches'] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.metricsSummary() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}
