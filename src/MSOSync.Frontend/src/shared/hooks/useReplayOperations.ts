import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  cancelReplay, createReplay, getReplayDetail, getReplayItems, replayKeys,
} from '../api/replay';
import { operationKeys } from '../api/operations';
import type { CreateReplayOperationRequest } from '../types/replay';
import { getErrorMessage } from '../utils/error';

export function useReplayOperation(id: string | null) {
  return useQuery({
    queryKey: replayKeys.detail(id ?? ''),
    queryFn:  ({ signal }) => getReplayDetail(id!, { signal }),
    enabled:  id !== null,
    refetchInterval: 5_000,
  });
}

export function useReplayItems(id: string | null) {
  return useQuery({
    queryKey: replayKeys.items(id ?? ''),
    queryFn:  ({ signal }) => getReplayItems(id!, undefined, { signal }),
    enabled:  id !== null,
    refetchInterval: 5_000,
  });
}

export function useCreateReplay() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateReplayOperationRequest) => createReplay(body),
    onSuccess: (data) => {
      toast.success(`Replay started — ${data.itemCount} item(s) queued`);
      void qc.invalidateQueries({ queryKey: replayKeys.all });
      void qc.invalidateQueries({ queryKey: operationKeys.all });
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  });
}

export function useCancelReplay() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => cancelReplay(id),
    onSuccess: () => {
      toast.success('Replay cancelled');
      void qc.invalidateQueries({ queryKey: replayKeys.all });
      void qc.invalidateQueries({ queryKey: operationKeys.all });
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  });
}
