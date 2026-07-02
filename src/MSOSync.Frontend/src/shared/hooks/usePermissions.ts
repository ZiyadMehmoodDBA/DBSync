import { useQuery } from '@tanstack/react-query';
import { getMyPermissions } from '../api/permissions';
import { queryKeys } from '../queryKeys';
import type { PermissionKey } from '../types/permissions';

export function usePermissions() {
  return useQuery({
    queryKey: queryKeys.permissions(),
    queryFn:  getMyPermissions,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

export function useHasPermission(key: PermissionKey): boolean {
  const { data } = usePermissions();
  if (data === undefined) return false;
  return data.permissions.includes(key);
}
