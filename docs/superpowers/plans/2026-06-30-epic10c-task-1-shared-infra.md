# Epic 10C — Task 1: Shared Infrastructure

> Master plan: `docs/superpowers/plans/2026-06-30-epic10c-operational-actions.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10c-operational-actions-design.md`

**Goal:** Install Sonner + shadcn AlertDialog/DropdownMenu, create `getErrorMessage` utility, create three presentation-only shared action components, mount `<Toaster>` in providers.

**Files to create/modify** (all under `src/MSOSync.Frontend/`):

Create:
- `src/shared/utils/error.ts`
- `src/shared/components/actions/ConfirmDialog.tsx`
- `src/shared/components/actions/ActionMenu.tsx`
- `src/shared/components/actions/ActionButton.tsx`
- `src/shared/components/actions/index.ts`
- `src/components/ui/dropdown-menu.tsx` ← shadcn generated
- `src/components/ui/alert-dialog.tsx` ← shadcn generated

Modify:
- `src/app/providers.tsx` — add `<Toaster>`
- `package.json` — `sonner` added by npm

**Global constraints for this task:**
- Shared action components: no React Query imports, no API imports, no toast calls, no business logic — presentation only
- Relative imports only — no `@/` alias
- No `any`

---

- [ ] **Step 1: Install sonner**

Run from repo root (`D:\MSOSync\`):
```bash
npm --prefix src/MSOSync.Frontend install sonner
```
Expected: `sonner` appears in `src/MSOSync.Frontend/package.json` dependencies.

- [ ] **Step 2: Install shadcn dropdown-menu and alert-dialog**

Run from repo root:
```bash
cd src/MSOSync.Frontend && npx shadcn@latest add dropdown-menu alert-dialog
```
Accept any prompts. Expected: two new files appear:
- `src/MSOSync.Frontend/src/components/ui/dropdown-menu.tsx`
- `src/MSOSync.Frontend/src/components/ui/alert-dialog.tsx`

- [ ] **Step 3: Create `src/shared/utils/error.ts`**

```typescript
import type { AxiosError } from 'axios';

interface ApiErrorBody {
  detail?: string;
  message?: string;
}

export function getErrorMessage(error: unknown): string {
  const axiosError = error as AxiosError<ApiErrorBody>;
  if (axiosError?.response?.data) {
    const { data } = axiosError.response;
    if (data.detail) return data.detail;
    if (data.message) return data.message;
  }
  if (error instanceof Error) return error.message;
  return 'An unexpected error occurred.';
}
```

- [ ] **Step 4: Create `src/shared/components/actions/ConfirmDialog.tsx`**

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
import { cn } from '../../../lib/utils';

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel?: string;
  variant?: 'default' | 'destructive';
  loading?: boolean;
  onConfirm: () => void;
  onOpenChange: (open: boolean) => void;
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = 'Confirm',
  variant = 'default',
  loading = false,
  onConfirm,
  onOpenChange,
}: ConfirmDialogProps) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={loading}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            onClick={onConfirm}
            disabled={loading}
            className={cn(
              variant === 'destructive' &&
                'bg-destructive text-destructive-foreground hover:bg-destructive/90',
            )}
          >
            {loading ? 'Working…' : confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 5: Create `src/shared/components/actions/ActionMenu.tsx`**

```tsx
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '../../../components/ui/dropdown-menu';
import { Button } from '../../../components/ui/button';

interface ActionMenuItem {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  variant?: 'default' | 'destructive';
}

interface ActionMenuProps {
  items: ActionMenuItem[];
}

export function ActionMenu({ items }: ActionMenuProps) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="sm" className="h-7 w-7 p-0">
          ⋮
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {items.map((item) => (
          <DropdownMenuItem
            key={item.label}
            onClick={item.onClick}
            disabled={item.disabled}
            className={item.variant === 'destructive' ? 'text-destructive' : undefined}
          >
            {item.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

- [ ] **Step 6: Create `src/shared/components/actions/ActionButton.tsx`**

```tsx
import { Button } from '../../../components/ui/button';

interface ActionButtonProps {
  label: string;
  onClick: () => void;
  loading?: boolean;
  variant?: 'default' | 'destructive';
}

export function ActionButton({
  label,
  onClick,
  loading = false,
  variant = 'default',
}: ActionButtonProps) {
  return (
    <Button
      variant={variant === 'destructive' ? 'destructive' : 'outline'}
      size="sm"
      onClick={onClick}
      disabled={loading}
      className="h-7 text-xs"
    >
      {loading ? 'Working…' : label}
    </Button>
  );
}
```

- [ ] **Step 7: Create `src/shared/components/actions/index.ts`**

```typescript
export * from './ActionButton';
export * from './ActionMenu';
export * from './ConfirmDialog';
```

- [ ] **Step 8: Modify `src/app/providers.tsx` — add Toaster**

Current file:
```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import { AuthProvider } from '../features/auth/AuthProvider';

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000 } },
});

export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>{children}</AuthProvider>
    </QueryClientProvider>
  );
}
```

Replace with:
```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import { Toaster } from 'sonner';
import { AuthProvider } from '../features/auth/AuthProvider';

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000 } },
});

export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>{children}</AuthProvider>
      <Toaster richColors closeButton position="bottom-right" />
    </QueryClientProvider>
  );
}
```

- [ ] **Step 9: Verify build and tests pass**

Run from repo root:
```bash
cd src/MSOSync.Frontend && npm run build
```
Expected: exits 0, no TypeScript errors.

```bash
npm test -- --run
```
Expected: 12/12 pass.

- [ ] **Step 10: Commit**

```bash
git add src/MSOSync.Frontend/package.json
git add src/MSOSync.Frontend/package-lock.json
git add src/MSOSync.Frontend/src/components/ui/dropdown-menu.tsx
git add src/MSOSync.Frontend/src/components/ui/alert-dialog.tsx
git add src/MSOSync.Frontend/src/shared/utils/error.ts
git add src/MSOSync.Frontend/src/shared/components/actions/ConfirmDialog.tsx
git add src/MSOSync.Frontend/src/shared/components/actions/ActionMenu.tsx
git add src/MSOSync.Frontend/src/shared/components/actions/ActionButton.tsx
git add src/MSOSync.Frontend/src/shared/components/actions/index.ts
git add src/MSOSync.Frontend/src/app/providers.tsx
git commit -m "feat(10c): add shared action components + Sonner toaster"
```

---

## Report Contract

Write report to the path given by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files created (count) and npm packages installed
- Build result
- Test result (N/12 pass)
- Any concerns
