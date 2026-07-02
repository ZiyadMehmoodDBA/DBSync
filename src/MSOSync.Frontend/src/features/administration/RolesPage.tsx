import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import { usePermissionCatalog, useRoles, useGrantPermission, useRevokePermission, useResetRole, useCopyFrom } from '../../shared/hooks/useRoles';
import { RolePermissionsCard } from './components/RolePermissionsCard';
import type { PermissionKey } from '../../shared/types/permissions';

export function RolesPage() {
  const { data: catalog = [], isLoading: catalogLoading } = usePermissionCatalog();
  const { data: roles = [], isLoading: rolesLoading } = useRoles();
  const grantMutation   = useGrantPermission();
  const revokeMutation  = useRevokePermission();
  const resetMutation   = useResetRole();
  const copyFromMutation = useCopyFrom();

  const allRoleNames = roles.map(r => r.roleName);

  const makeGrant = (roleName: string) => async (key: PermissionKey) => {
    try {
      await grantMutation.mutateAsync({ roleName, key });
    } catch (err) {
      toast.error(getErrorMessage(err));
    }
  };

  const makeRevoke = (roleName: string) => async (key: PermissionKey) => {
    try {
      await revokeMutation.mutateAsync({ roleName, key });
    } catch (err) {
      toast.error(getErrorMessage(err));
    }
  };

  const makeReset = (roleName: string) => async () => {
    try {
      await resetMutation.mutateAsync(roleName);
      toast.success(`${roleName} permissions reset to defaults`);
    } catch (err) {
      toast.error(getErrorMessage(err));
    }
  };

  const makeCopyFrom = (targetRole: string) => async (sourceRole: string) => {
    try {
      await copyFromMutation.mutateAsync({ targetRole, sourceRole });
      toast.success(`${targetRole} permissions copied from ${sourceRole}`);
    } catch (err) {
      toast.error(getErrorMessage(err));
    }
  };

  if (catalogLoading || rolesLoading) {
    return (
      <div className="flex flex-col gap-4 p-6">
        <h1 className="text-2xl font-semibold">Roles</h1>
        <p className="text-sm text-neutral-500">Loading…</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Roles</h1>
        <p className="text-sm text-neutral-500 mt-1">
          Manage per-role permissions. Changes take effect within 60 seconds.
        </p>
      </div>
      <div className="flex gap-4 flex-wrap">
        {roles.map(role => (
          <RolePermissionsCard
            key={role.roleName}
            role={role}
            catalog={catalog}
            allRoleNames={allRoleNames}
            onGrant={makeGrant(role.roleName)}
            onRevoke={makeRevoke(role.roleName)}
            onReset={makeReset(role.roleName)}
            onCopyFrom={makeCopyFrom(role.roleName)}
          />
        ))}
      </div>
    </div>
  );
}
