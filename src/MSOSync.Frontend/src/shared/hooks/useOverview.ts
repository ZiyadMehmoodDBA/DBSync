import { useQuery } from '@tanstack/react-query';
import { fetchOverview, systemKeys } from '../api/system';

export function useOverview() {
  return useQuery({
    queryKey: systemKeys.overview,
    queryFn: fetchOverview,
    staleTime: 5_000,
    refetchOnWindowFocus: true,
  });
}
