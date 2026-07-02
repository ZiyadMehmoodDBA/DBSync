import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getPermissionCatalog,
  getRoles,
  getRoleDetail,
  grantPermission,
  revokePermission,
  resetRole,
  copyFrom,
} from '../api/permissions';
import { queryKeys } from '../queryKeys';
import type { PermissionKey } from '../types/permissions';

export function usePermissionCatalog() {
  return useQuery({
    queryKey: queryKeys.permissionCatalog(),
    queryFn:  getPermissionCatalog,
    staleTime: Infinity,
  });
}

export function useRoles() {
  return useQuery({
    queryKey: queryKeys.roles(),
    queryFn:  getRoles,
  });
}

export function useRoleDetail(roleName: string) {
  return useQuery({
    queryKey: queryKeys.role(roleName),
    queryFn:  () => getRoleDetail(roleName),
  });
}

export function useGrantPermission() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ roleName, key }: { roleName: string; key: PermissionKey }) =>
      grantPermission(roleName, key),
    onSuccess: (_data, { roleName }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(roleName) });
    },
  });
}

export function useRevokePermission() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ roleName, key }: { roleName: string; key: PermissionKey }) =>
      revokePermission(roleName, key),
    onSuccess: (_data, { roleName }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(roleName) });
    },
  });
}

export function useResetRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (roleName: string) => resetRole(roleName),
    onSuccess: (_data, roleName) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(roleName) });
    },
  });
}

export function useCopyFrom() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ targetRole, sourceRole }: { targetRole: string; sourceRole: string }) =>
      copyFrom(targetRole, sourceRole),
    onSuccess: (_data, { targetRole }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(targetRole) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.roles() });
    },
  });
}
