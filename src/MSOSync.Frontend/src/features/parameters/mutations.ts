import { useMutation, useQueryClient } from '@tanstack/react-query';
import { updateParameter } from '../../shared/api/parameters';
import { queryKeys } from '../../shared/queryKeys';

export function useUpdateParameterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameter(name, value),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.parameters() });
    },
    // no onError — dialog handles it
  });
}
