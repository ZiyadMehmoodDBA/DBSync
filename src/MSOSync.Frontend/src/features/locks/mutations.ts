import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { releaseLock } from '../../shared/api/locks';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

export function useReleaseLockMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (lockName: string) => releaseLock(lockName),
    onSuccess: () => {
      toast.success('Lock released');
      void queryClient.invalidateQueries({ queryKey: queryKeys.locks() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}
