import type { ReactNode } from 'react';
import { usePermissions, useHasPermission } from '../../shared/hooks/usePermissions';
import type { PermissionKey } from '../../shared/types/permissions';
import { PermissionDeniedPage } from './PermissionDeniedPage';

interface Props {
  permissionKey: PermissionKey;
  children: ReactNode;
}

export function PermissionGuard({ permissionKey, children }: Props) {
  const { isLoading } = usePermissions();
  const can = useHasPermission(permissionKey);

  if (isLoading) return null;
  if (!can) return <PermissionDeniedPage />;
  return <>{children}</>;
}
