import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createRouter, updateRouter, deleteRouter } from '../../shared/api/routers';
import type { CreateRouterRequest, UpdateRouterRequest } from '../../shared/api/routers';
import { queryKeys } from '../../shared/queryKeys';

function invalidateRouterRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.routers() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useCreateRouterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateRouterRequest) => createRouter(data),
    onSuccess: () => { invalidateRouterRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateRouterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ routerId, data }: { routerId: string; data: UpdateRouterRequest }) =>
      updateRouter(routerId, data),
    onSuccess: () => { invalidateRouterRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeleteRouterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (routerId: string) => deleteRouter(routerId),
    onSuccess: () => { invalidateRouterRelated(queryClient); },
    // no onError — caller handles it
  });
}
