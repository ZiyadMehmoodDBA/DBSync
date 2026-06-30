import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createUser, updateUser, deactivateUser } from '../../shared/api/users';
import type { CreateUserRequest, UpdateUserRequest } from '../../shared/api/users';
import { queryKeys } from '../../shared/queryKeys';

function invalidateUsers(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: ['users'] });
  void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
}

export function useCreateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateUserRequest) => createUser(data),
    onSuccess: () => { invalidateUsers(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, data }: { userId: number; data: UpdateUserRequest }) =>
      updateUser(userId, data),
    onSuccess: () => { invalidateUsers(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeactivateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: number) => deactivateUser(userId),
    onSuccess: () => { invalidateUsers(queryClient); },
    // no onError — caller handles it
  });
}
