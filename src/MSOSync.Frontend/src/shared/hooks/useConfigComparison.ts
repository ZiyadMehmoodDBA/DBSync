import { useQuery } from '@tanstack/react-query';
import { configCompareKeys, getConfigVersionDiff } from '../api/configComparison';

export function useConfigComparison(
  templateId: string | null,
  v1: number | null,
  v2: number | null,
) {
  return useQuery({
    queryKey: configCompareKeys.diff(templateId ?? '', v1 ?? 0, v2 ?? 0),
    queryFn:  ({ signal }) => getConfigVersionDiff(templateId!, v1!, v2!, { signal }),
    enabled:  templateId !== null && v1 !== null && v2 !== null && v1 !== v2,
    staleTime: 60_000,
  });
}
