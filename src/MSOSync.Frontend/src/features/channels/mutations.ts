import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createChannel, updateChannel, deleteChannel } from '../../shared/api/channels';
import type { CreateChannelRequest, UpdateChannelRequest } from '../../shared/api/channels';
import { queryKeys } from '../../shared/queryKeys';

function invalidateChannelRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.channels() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGraph() });
}

export function useCreateChannelMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateChannelRequest) => createChannel(data),
    onSuccess: () => { invalidateChannelRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateChannelMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ channelId, data }: { channelId: string; data: UpdateChannelRequest }) =>
      updateChannel(channelId, data),
    onSuccess: () => { invalidateChannelRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeleteChannelMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (channelId: string) => deleteChannel(channelId),
    onSuccess: () => { invalidateChannelRelated(queryClient); },
    // no onError — caller handles it
  });
}
