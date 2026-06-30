import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getParameters, getParameterDescriptors } from '../../shared/api/parameters';

export function useParameters() {
  return useQuery({
    queryKey: queryKeys.parameters(),
    queryFn: getParameters,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}

export function useParameterDescriptors() {
  return useQuery({
    queryKey: queryKeys.parameterDescriptors(),
    queryFn: getParameterDescriptors,
    staleTime: 300_000,
    refetchOnWindowFocus: false,
  });
}
