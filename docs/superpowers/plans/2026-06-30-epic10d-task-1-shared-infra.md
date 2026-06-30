# Epic 10D — Task 1: Shared Form Infrastructure

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install npm/shadcn dependencies and create four shared presentation components that all feature dialogs use.

**Architecture:** New dir `src/MSOSync.Frontend/src/shared/components/forms/`. Components are presentation-only — no React Query, no API imports, no toast calls. They receive props and render UI only.

**Tech Stack:** React 19, TypeScript strict, shadcn Dialog, shadcn Form, Tailwind CSS.

## Global Constraints

- TypeScript strict; no `any`; relative imports only (no `@/` alias in our code)
- Shared form components are presentation-only: no React Query, no API imports, no toast calls
- No new Vitest tests — build clean = done for this task
- `npm run build` from `src/MSOSync.Frontend/` must exit 0 after this task

---

## Files

**Create:**
- `src/MSOSync.Frontend/src/shared/components/forms/EntityDialog.tsx`
- `src/MSOSync.Frontend/src/shared/components/forms/FormActions.tsx`
- `src/MSOSync.Frontend/src/shared/components/forms/FormError.tsx`
- `src/MSOSync.Frontend/src/shared/components/forms/FormSection.tsx`
- `src/MSOSync.Frontend/src/shared/components/forms/index.ts`

**No existing files modified in this task.**

---

## Steps

- [ ] **Step 1: Verify working directory and existing shadcn components**

Run from `src/MSOSync.Frontend/`:
```bash
ls src/components/ui/
```
Expected output includes: `button.tsx`, `input.tsx`, `label.tsx`, `alert-dialog.tsx`, `dropdown-menu.tsx`.
Missing (will be added in Step 3): `dialog.tsx`, `form.tsx`, `select.tsx`, `checkbox.tsx`, `textarea.tsx`.

- [ ] **Step 2: Install npm dependencies**

Run from `src/MSOSync.Frontend/`:
```bash
npm install react-hook-form zod @hookform/resolvers
```

Expected: packages added to `package.json`. No peer dependency errors.

- [ ] **Step 3: Add shadcn components via CLI**

Run from `src/MSOSync.Frontend/`:
```bash
npx shadcn@latest add dialog
npx shadcn@latest add form
npx shadcn@latest add select
npx shadcn@latest add checkbox
npx shadcn@latest add textarea
```

Each command creates a new file in `src/components/ui/`. Verify after:
```bash
ls src/components/ui/
```
Expected additions: `dialog.tsx`, `form.tsx`, `select.tsx`, `checkbox.tsx`, `textarea.tsx`.

- [ ] **Step 4: Create EntityDialog.tsx**

Create `src/MSOSync.Frontend/src/shared/components/forms/EntityDialog.tsx`:

```tsx
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '../../../components/ui/dialog';

interface EntityDialogProps {
  open: boolean;
  title: string;
  description?: string;
  onOpenChange: (open: boolean) => void;
  children: React.ReactNode;
}

export function EntityDialog({
  open,
  title,
  description,
  onOpenChange,
  children,
}: EntityDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>
        <div className="flex flex-col gap-4 overflow-y-auto max-h-[70vh] py-2">
          {children}
        </div>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 5: Create FormActions.tsx**

Create `src/MSOSync.Frontend/src/shared/components/forms/FormActions.tsx`:

```tsx
import { Button } from '../../../components/ui/button';

interface FormActionsProps {
  loading: boolean;
  onCancel: () => void;
  submitLabel?: string;
}

export function FormActions({
  loading,
  onCancel,
  submitLabel = 'Save',
}: FormActionsProps) {
  return (
    <div className="flex justify-end gap-2 pt-2">
      <Button type="button" variant="outline" onClick={onCancel} disabled={loading}>
        Cancel
      </Button>
      <Button type="submit" disabled={loading}>
        {loading ? 'Saving…' : submitLabel}
      </Button>
    </div>
  );
}
```

- [ ] **Step 6: Create FormError.tsx**

Create `src/MSOSync.Frontend/src/shared/components/forms/FormError.tsx`:

```tsx
interface FormErrorProps {
  error: string | null;
}

export function FormError({ error }: FormErrorProps) {
  if (!error) return null;
  return (
    <div className="rounded-md bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-300">
      {error}
    </div>
  );
}
```

- [ ] **Step 7: Create FormSection.tsx**

Create `src/MSOSync.Frontend/src/shared/components/forms/FormSection.tsx`:

```tsx
interface FormSectionProps {
  title: string;
  children: React.ReactNode;
}

export function FormSection({ title, children }: FormSectionProps) {
  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-sm font-medium text-neutral-700 dark:text-neutral-300 border-b border-neutral-200 dark:border-neutral-700 pb-1">
        {title}
      </h3>
      {children}
    </div>
  );
}
```

- [ ] **Step 8: Create index.ts barrel**

Create `src/MSOSync.Frontend/src/shared/components/forms/index.ts`:

```ts
export * from './EntityDialog';
export * from './FormActions';
export * from './FormError';
export * from './FormSection';
```

- [ ] **Step 9: Verify build clean**

Run from `src/MSOSync.Frontend/`:
```bash
npm run build
```
Expected: exits 0, no TypeScript errors.

- [ ] **Step 10: Commit**

```bash
git add src/MSOSync.Frontend/package.json src/MSOSync.Frontend/package-lock.json
git add src/MSOSync.Frontend/src/components/ui/dialog.tsx
git add src/MSOSync.Frontend/src/components/ui/form.tsx
git add src/MSOSync.Frontend/src/components/ui/select.tsx
git add src/MSOSync.Frontend/src/components/ui/checkbox.tsx
git add src/MSOSync.Frontend/src/components/ui/textarea.tsx
git add src/MSOSync.Frontend/src/shared/components/forms/EntityDialog.tsx
git add src/MSOSync.Frontend/src/shared/components/forms/FormActions.tsx
git add src/MSOSync.Frontend/src/shared/components/forms/FormError.tsx
git add src/MSOSync.Frontend/src/shared/components/forms/FormSection.tsx
git add src/MSOSync.Frontend/src/shared/components/forms/index.ts
git commit -m "feat(10d): add shared form infrastructure and install form deps"
```
