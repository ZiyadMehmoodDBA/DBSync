import { useState } from 'react';
import {
  Card,
  CardHeader,
  CardTitle,
  CardContent,
  CardFooter,
} from '../../../components/ui/card';
import { Button } from '../../../components/ui/button';
import { Checkbox } from '../../../components/ui/checkbox';
import { Separator } from '../../../components/ui/separator';
import { CopyFromDialog } from './CopyFromDialog';
import { ResetRoleDialog } from './ResetRoleDialog';
import type { PermissionDto, RolePermissionsDto, PermissionKey } from '../../../shared/types/permissions';
import { PermissionKeys } from '../../../shared/types/permissions';

interface Props {
  role: RolePermissionsDto;
  catalog: PermissionDto[];
  allRoleNames: string[];
  onGrant: (key: PermissionKey) => Promise<void>;
  onRevoke: (key: PermissionKey) => Promise<void>;
  onCopyFrom: (sourceRole: string) => Promise<void>;
  onReset: () => Promise<void>;
}

export function RolePermissionsCard({
  role,
  catalog,
  allRoleNames,
  onGrant,
  onRevoke,
  onCopyFrom,
  onReset,
}: Props) {
  const [copyOpen, setCopyOpen] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [pendingKey, setPendingKey] = useState<PermissionKey | null>(null);
  const [isCopyPending, setIsCopyPending] = useState(false);
  const [isResetPending, setIsResetPending] = useState(false);

  const grantedKeys = new Set(role.permissions.map(p => p.permissionKey));

  // Group catalog by category — category order is determined by first appearance in catalog
  const categories: string[] = [];
  const byCategory: Record<string, PermissionDto[]> = {};
  for (const perm of catalog) {
    if (!byCategory[perm.category]) {
      categories.push(perm.category);
      byCategory[perm.category] = [];
    }
    byCategory[perm.category].push(perm);
  }

  const isProtected = (key: PermissionKey) =>
    role.roleName === 'ADMIN' && key === PermissionKeys.ManageUsers;

  const handleToggle = async (key: PermissionKey, checked: boolean) => {
    if (isProtected(key)) return;
    setPendingKey(key);
    try {
      if (checked) {
        await onGrant(key);
      } else {
        await onRevoke(key);
      }
    } finally {
      setPendingKey(null);
    }
  };

  const handleCopyFrom = async (sourceRole: string) => {
    setIsCopyPending(true);
    try {
      await onCopyFrom(sourceRole);
      setCopyOpen(false);
    } finally {
      setIsCopyPending(false);
    }
  };

  const handleReset = async () => {
    setIsResetPending(true);
    try {
      await onReset();
      setResetOpen(false);
    } finally {
      setIsResetPending(false);
    }
  };

  return (
    <>
      <Card className="flex-1 min-w-64">
        <CardHeader>
          <CardTitle>{role.roleName}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {categories.map((category, idx) => (
            <div key={category}>
              {idx > 0 && <Separator className="mb-3" />}
              <p className="text-xs font-semibold uppercase tracking-wide text-neutral-500 mb-2">
                {category}
              </p>
              <div className="flex flex-col gap-2">
                {byCategory[category].map(perm => {
                  const checked = grantedKeys.has(perm.permissionKey);
                  const protected_ = isProtected(perm.permissionKey);
                  const isPending = pendingKey === perm.permissionKey;
                  return (
                    <label
                      key={perm.permissionKey}
                      className="flex items-start gap-2 cursor-pointer"
                    >
                      <Checkbox
                        checked={checked}
                        disabled={protected_ || isPending}
                        onCheckedChange={(val) => void handleToggle(perm.permissionKey, !!val)}
                        className="mt-0.5 shrink-0"
                      />
                      <span className="flex flex-col">
                        <span className="text-sm font-mono text-xs text-neutral-700 dark:text-neutral-300">
                          {perm.permissionKey}
                        </span>
                        <span className="text-xs text-neutral-500">{perm.description}</span>
                      </span>
                    </label>
                  );
                })}
              </div>
            </div>
          ))}
        </CardContent>
        <CardFooter className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            className="flex-1"
            onClick={() => setCopyOpen(true)}
          >
            Copy From…
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="flex-1"
            onClick={() => setResetOpen(true)}
          >
            Reset
          </Button>
        </CardFooter>
      </Card>

      <CopyFromDialog
        open={copyOpen}
        targetRoleName={role.roleName}
        allRoleNames={allRoleNames}
        isPending={isCopyPending}
        onOpenChange={setCopyOpen}
        onConfirm={handleCopyFrom}
      />

      <ResetRoleDialog
        open={resetOpen}
        roleName={role.roleName}
        isPending={isResetPending}
        onOpenChange={setResetOpen}
        onConfirm={() => void handleReset()}
      />
    </>
  );
}
