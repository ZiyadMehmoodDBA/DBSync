import { useQuery } from '@tanstack/react-query';
import { fetchWorkers, systemKeys } from '../api/system';

export function useWorkers() {
  return useQuery({
    queryKey: systemKeys.workers,
    queryFn: fetchWorkers,
    staleTime: 10_000,
    refetchOnWindowFocus: true,
  });
}
