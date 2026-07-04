import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../queryKeys';
import {
  createExportJob,
  getExportJobs,
  deleteExportJob,
} from '../api/exportJobs';
import type { CreateExportJobRequest } from '../types/export';

export function useExportJobs() {
  return useQuery({
    queryKey: queryKeys.exportJobs(),
    queryFn: getExportJobs,
    refetchOnWindowFocus: false,
    staleTime: 30_000,
  });
}

export function useCreateExportJobMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateExportJobRequest) => createExportJob(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.exportJobs() });
    },
  });
}

export function useDeleteExportJobMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (jobId: string) => deleteExportJob(jobId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.exportJobs() });
    },
  });
}
