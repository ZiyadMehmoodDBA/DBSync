import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  fetchOperations,
  fetchOperationDetail,
  cancelOperation,
  retryOperation,
  operationKeys,
} from '../api/operations';
import { getErrorMessage } from '../utils/error';
import type { OperationFilter } from '../types/operations';

export function useOperations(filter: OperationFilter) {
  return useQuery({
    queryKey: operationKeys.list(filter),
    queryFn: () => fetchOperations(filter),
    staleTime: 10_000,
    refetchOnWindowFocus: true,
  });
}

export function useOperationDetail(id: string) {
  return useQuery({
    queryKey: operationKeys.detail(id),
    queryFn: () => fetchOperationDetail(id),
    staleTime: 5_000,
    enabled: !!id,
  });
}

export function useCancelOperation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: cancelOperation,
    onSuccess: () => {
      toast.success('Operation cancelled');
      void qc.invalidateQueries({ queryKey: operationKeys.all });
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  });
}

export function useRetryOperation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: retryOperation,
    onSuccess: () => {
      toast.success('Operation retried');
      void qc.invalidateQueries({ queryKey: operationKeys.all });
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  });
}
