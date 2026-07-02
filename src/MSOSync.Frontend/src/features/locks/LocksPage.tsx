import { LocksGrid } from './LocksGrid';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

export function LocksPage() {
  const canRelease = useHasPermission(PermissionKeys.ReleaseLocks);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Locks</h1>
      <LocksGrid canRelease={canRelease} />
    </div>
  );
}
