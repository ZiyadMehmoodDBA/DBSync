# Task 5: Admin Roles Page

**Part of:** Epic 11F — Fine-Grained RBAC  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11f-rbac-design.md`  
**Depends on:** Task 3 (useRoles, usePermissionCatalog, useGrantPermission, etc. must exist) + Task 4 (PermissionGuard must exist; router + AppLayout patterns established)

## Files

**Create:**
- `src/MSOSync.Frontend/src/features/administration/RolesPage.tsx`
- `src/MSOSync.Frontend/src/features/administration/components/RolePermissionsCard.tsx`
- `src/MSOSync.Frontend/src/features/administration/components/CopyFromDialog.tsx`
- `src/MSOSync.Frontend/src/features/administration/components/ResetRoleDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/app/router.tsx` — add `/administration/roles` route + import RolesPage
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add Roles to Administration sidebar group

## Interfaces Consumed (from Tasks 3–4)

```typescript
// From Task 3 hooks
import { usePermissionCatalog, useRoles, useGrantPermission, useRevokePermission, useResetRole, useCopyFrom } from '../../shared/hooks/useRoles';
// adjust relative paths

// From Task 3 types
import type { PermissionDto, RolePermissionsDto, PermissionKey } from '../../shared/types/permissions';
import { PermissionKeys } from '../../shared/types/permissions';

// From Task 4
import { PermissionGuard } from '../auth/PermissionGuard';
// used in router.tsx to wrap the /administration/roles route
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum`
- All imports relative — no `@/`
- No new npm packages
- No switch/toggle component exists — use `<Checkbox>` from `../../components/ui/checkbox`
- MANAGE_USERS checkbox for ADMIN is permanently checked and `disabled` — cannot be toggled off (protected server-side too, but disable in UI as well)
- Roles page rendering must be category-generic: group permissions by `catalog[i].category`, do not hardcode category names
- Each role card gets its own grant/revoke handlers using the role's `roleName`
- Read each file before modifying — current structure matters
- Build env: frontend only

---

## Page Design

```
/administration/roles

┌─────────────────────────────────────────────────────────────┐
│ Roles                                             [3 cards]  │
├──────────────┬──────────────┬──────────────────────────────┤
│ ADMIN        │ OPERATOR     │ VIEWER                        │
│ (read-only)  │              │                               │
│              │              │                               │
│ ─ Data ────  │ ─ Data ────  │ ─ Data ──────────────────── │
│ ☑ VIEW_EVENTS│ ☑ VIEW_EVENTS│ ☑ VIEW_EVENTS               │
│ ☑ VIEW_METRIC│ ☑ VIEW_METRIC│ ☑ VIEW_METRICS              │
│ ☑ VIEW_AUDIT │ ☑ VIEW_AUDIT │ ☑ VIEW_AUDIT                │
│ ☑ VIEW_TOPOL │ ☑ VIEW_TOPOL │ ☑ VIEW_TOPOLOGY             │
│              │              │                               │
│ ─ Operations │ ─ Operations │ ─ Operations ─────────────── │
│ ☑ EXPORT_DAT │ ☑ EXPORT_DAT │ □ EXPORT_DATA               │
│ ...          │ ...          │ □ ...                         │
│              │              │                               │
│ [Copy From]  │ [Copy From]  │ [Copy From]                  │
│ [Reset]      │ [Reset]      │ [Reset]                       │
└──────────────┴──────────────┴──────────────────────────────┘
```

ADMIN card: all permissions checked; MANAGE_USERS is checked + disabled. All other ADMIN checkboxes remain interactive (admin can change their own role's other permissions).

---

- [ ] **Step 1: Create `src/MSOSync.Frontend/src/features/administration/components/CopyFromDialog.tsx`**

This dialog lets the user select a source role and copy its permissions to the current role.

```tsx
import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '../../../components/ui/dialog';
import { Button } from '../../../components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../../components/ui/select';

interface Props {
  open: boolean;
  targetRoleName: string;
  allRoleNames: string[];
  isPending: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (sourceRole: string) => void;
}

export function CopyFromDialog({
  open,
  targetRoleName,
  allRoleNames,
  isPending,
  onOpenChange,
  onConfirm,
}: Props) {
  const [sourceRole, setSourceRole] = useState('');
  const otherRoles = allRoleNames.filter(r => r !== targetRoleName);

  const handleConfirm = () => {
    if (!sourceRole) return;
    onConfirm(sourceRole);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Copy Permissions From</DialogTitle>
        </DialogHeader>
        <div className="py-2">
          <p className="text-sm text-neutral-500 mb-3">
            Replace <strong>{targetRoleName}</strong>'s permissions with those from:
          </p>
          <Select value={sourceRole} onValueChange={setSourceRole}>
            <SelectTrigger>
              <SelectValue placeholder="Select a role…" />
            </SelectTrigger>
            <SelectContent>
              {otherRoles.map(r => (
                <SelectItem key={r} value={r}>{r}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={handleConfirm} disabled={!sourceRole || isPending}>
            {isPending ? 'Copying…' : 'Copy Permissions'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 2: Create `src/MSOSync.Frontend/src/features/administration/components/ResetRoleDialog.tsx`**

```tsx
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '../../../components/ui/alert-dialog';

interface Props {
  open: boolean;
  roleName: string;
  isPending: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: () => void;
}

export function ResetRoleDialog({ open, roleName, isPending, onOpenChange, onConfirm }: Props) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Reset {roleName} to defaults?</AlertDialogTitle>
          <AlertDialogDescription>
            This will restore the default set of permissions for the <strong>{roleName}</strong> role.
            Any custom grants or revocations will be lost.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={onConfirm} disabled={isPending}>
            {isPending ? 'Resetting…' : 'Reset to Defaults'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 3: Create `src/MSOSync.Frontend/src/features/administration/components/RolePermissionsCard.tsx`**

This card displays one role's permissions grouped by category, with checkboxes to grant/revoke.

```tsx
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
```

- [ ] **Step 4: Create `src/MSOSync.Frontend/src/features/administration/RolesPage.tsx`**

```tsx
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
```

- [ ] **Step 5: Update `src/MSOSync.Frontend/src/app/router.tsx` — add `/administration/roles` route**

Read `router.tsx` fully first. After the Task 4 changes, it already imports `PermissionGuard` and `PermissionKeys`. Add the import for `RolesPage`:

Find the existing page imports (e.g., near `import { ProfilePage } from '../features/profile/ProfilePage';`) and add:
```tsx
import { RolesPage } from '../features/administration/RolesPage';
```

Add the route inside the `AppLayout` children, after the `users` route:
```tsx
{ path: 'administration/roles', element: <PermissionGuard permissionKey={PermissionKeys.ManageUsers}><RolesPage /></PermissionGuard> },
```

- [ ] **Step 6: Update `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add Roles to sidebar**

Read `AppLayout.tsx` fully first. After the Task 4 changes, the file already has `PermissionKeys` imported and `NavItem` has `requiredPermission?: PermissionKey`.

**Change 1** — add `ShieldCheck` to lucide-react imports. Find:
```tsx
import {
  ...
  Lock,
  User,
  ...
} from 'lucide-react';
```
Add `ShieldCheck` to the destructured list.

**Change 2** — add "Roles" to the Administration group in `NAV_GROUPS`. Find the Administration group's items array and add the Roles entry after Users:

```tsx
{ label: 'Users',      path: '/users',               icon: Users,        requiredPermission: PermissionKeys.ManageUsers },
{ label: 'Roles',      path: '/administration/roles', icon: ShieldCheck,  requiredPermission: PermissionKeys.ManageUsers },
```

Keep the remaining Administration items (Parameters, Audit, Locks) unchanged.

**Change 3** — update the `permMap` in `NavGroup` to include `ManageUsers` correctly. In Task 4, `permMap` was added. Verify it includes:
```tsx
[PermissionKeys.ManageUsers]: canManageUsers,
```
If that's already present from Task 4, no change needed.

- [ ] **Step 7: Build check**

```pwsh
cd src/MSOSync.Frontend
npm run build 2>&1 | Select-Object -Last 20
```

Expected: 0 TypeScript errors. Fix any type errors before proceeding. Common issues:
- `Checkbox` `onCheckedChange` receives `boolean | 'indeterminate'` — cast with `!!val`
- Relative import path depth — verify each `../../../` is correct from the component file location

- [ ] **Step 8: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/features/administration/RolesPage.tsx `
  src/MSOSync.Frontend/src/features/administration/components/RolePermissionsCard.tsx `
  src/MSOSync.Frontend/src/features/administration/components/CopyFromDialog.tsx `
  src/MSOSync.Frontend/src/features/administration/components/ResetRoleDialog.tsx `
  src/MSOSync.Frontend/src/app/router.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx

git commit -m "feat(11f): add admin Roles page with RolePermissionsCard + CopyFromDialog + ResetRoleDialog"
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Build: <result>
Concerns: <none or list>
```
