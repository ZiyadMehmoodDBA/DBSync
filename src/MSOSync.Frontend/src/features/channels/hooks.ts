import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getChannels } from '../../shared/api/channels';

export function useChannels() {
  return useQuery({
    queryKey: queryKeys.channels(),
    queryFn: getChannels,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
