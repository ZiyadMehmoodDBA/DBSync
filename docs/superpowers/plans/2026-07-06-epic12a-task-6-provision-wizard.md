# Epic 12A Task 6: Provision Wizard Frontend

> **For agentic workers:** This is Task 6 of 7. Tasks 4 and 5 must be complete. The `ProvisionTab` stub exists; this task replaces it with the full 5-step wizard, sessionStorage draft persistence, and package download.

**Goal:** Build the full provisioning wizard (5 steps: Node Type → Credentials → Network → Review → Complete), sessionStorage draft with version-guarded envelope, `useProvision` mutation hook, `useProvisionPackage` download hook, and token one-time display on Step 5.

## Global Constraints

- React 19, TanStack Query v5 — no new npm packages
- Wizard draft envelope: `{ "version": 1, "draft": {...} }` stored under key `"msosync:wizard:provision"`
- On mount: read envelope; if `version !== 1`, discard silently and start fresh; if matches and `draft` exists, offer "Resume draft?" toast via `sonner`
- Draft saved on every step advance; cleared on Step 5 success, Cancel, or tab navigation away from Provision
- Token: masked field with reveal/copy; one-time display warning; if page refresh with token lost, show recovery message
- `useProvisionPackage` triggers file download (`<a download>` click pattern)
- TypeScript strict mode: no `any`
- shadcn/ui components only (Button, Input, Label, RadioGroup, Select if installed; otherwise plain HTML with Tailwind)

## Files

**Create:**
- `src/MSOSync.Frontend/src/features/node-management/hooks/useProvision.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useProvisionPackage.ts`
- `src/MSOSync.Frontend/src/features/node-management/provision/components/ProvisionWizard.tsx`
- `src/MSOSync.Frontend/src/features/node-management/provision/steps/Step1NodeType.tsx`
- `src/MSOSync.Frontend/src/features/node-management/provision/steps/Step2Credentials.tsx`
- `src/MSOSync.Frontend/src/features/node-management/provision/steps/Step3Network.tsx`
- `src/MSOSync.Frontend/src/features/node-management/provision/steps/Step4Review.tsx`
- `src/MSOSync.Frontend/src/features/node-management/provision/steps/Step5Complete.tsx`

**Modify (replace stub):**
- `src/MSOSync.Frontend/src/features/node-management/provision/components/ProvisionTab.tsx`

## Interfaces Consumed

```typescript
// From types/provision.ts (Task 4)
ProvisionWizardDraft, ProvisionRequest, ProvisionResult, ProvisionPackageRequest
loadWizardDraft(), saveWizardDraft(draft), clearWizardDraft()

// From NodeManagementProvider (Task 4)
useNodeManagement(): { wizardDraft, setWizardDraft }

// From nodeManagementApi.ts (Task 5)
provision(request: ProvisionRequest): Promise<ProvisionResult>
downloadProvisionPackage(request: ProvisionPackageRequest): Promise<Blob>
```

---

## Steps

- [ ] **Step 1: Create useProvision hook**

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useProvision.ts
import { useMutation } from '@tanstack/react-query';
import { provision } from '../api/nodeManagementApi';
import type { ProvisionRequest, ProvisionResult } from '../types/provision';

export function useProvision() {
  return useMutation<ProvisionResult, Error, ProvisionRequest>({
    mutationFn: provision,
  });
}
```

- [ ] **Step 2: Create useProvisionPackage hook**

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useProvisionPackage.ts
import { useMutation } from '@tanstack/react-query';
import { downloadProvisionPackage } from '../api/nodeManagementApi';
import type { ProvisionPackageRequest } from '../types/provision';

export function useProvisionPackage() {
  return useMutation({
    mutationFn: async (request: ProvisionPackageRequest) => {
      const blob = await downloadProvisionPackage(request);
      const url  = URL.createObjectURL(blob);
      const a    = document.createElement('a');
      a.href     = url;
      a.download = `msosync-node-${request.nodeId}.zip`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    },
  });
}
```

- [ ] **Step 3: Create Step1NodeType**

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/steps/Step1NodeType.tsx
import { Button } from '../../../../components/ui/button';
import type { ProvisionWizardDraft, NodeType } from '../../types/provision';

interface Props {
  draft:    ProvisionWizardDraft;
  onChange: (patch: Partial<ProvisionWizardDraft>) => void;
  onNext:   () => void;
}

export function Step1NodeType({ draft, onChange, onNext }: Props) {
  const options: { value: NodeType; label: string; description: string }[] = [
    { value: 'source', label: 'Hub (source)', description: 'Originates data and pushes to targets.' },
    { value: 'target', label: 'Leaf (target)', description: 'Receives and applies data from a hub.' },
  ];

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 1: Node Type</h2>
        <p className="text-sm text-neutral-500 mt-1">
          Choose whether this node will originate or receive sync data.
        </p>
      </div>
      <div className="space-y-3">
        {options.map(opt => (
          <label
            key={opt.value}
            className={`flex items-start gap-3 rounded-lg border p-4 cursor-pointer transition-colors ${
              draft.nodeType === opt.value
                ? 'border-blue-500 bg-blue-50 dark:bg-blue-950/20'
                : 'border-neutral-200 dark:border-neutral-700 hover:bg-neutral-50 dark:hover:bg-neutral-800/50'
            }`}
          >
            <input
              type="radio"
              name="nodeType"
              value={opt.value}
              checked={draft.nodeType === opt.value}
              onChange={() => onChange({ nodeType: opt.value })}
              className="mt-0.5"
            />
            <div>
              <p className="font-medium text-sm">{opt.label}</p>
              <p className="text-xs text-neutral-500">{opt.description}</p>
            </div>
          </label>
        ))}
      </div>
      <div className="space-y-2">
        <label className="text-sm font-medium">Description (optional)</label>
        <textarea
          className="w-full rounded-md border px-3 py-2 text-sm dark:bg-neutral-900 dark:border-neutral-700"
          rows={2}
          placeholder="Describe the node's purpose…"
          value={draft.description ?? ''}
          onChange={e => onChange({ description: e.target.value || undefined })}
        />
      </div>
      <div className="flex justify-end">
        <Button onClick={onNext} disabled={!draft.nodeType}>
          Next
        </Button>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Create Step2Credentials**

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/steps/Step2Credentials.tsx
import { Button } from '../../../../components/ui/button';
import type { ProvisionWizardDraft } from '../../types/provision';

interface Props {
  draft:    ProvisionWizardDraft;
  onChange: (patch: Partial<ProvisionWizardDraft>) => void;
  onNext:   () => void;
  onBack:   () => void;
}

export function Step2Credentials({ draft, onChange, onNext, onBack }: Props) {
  const canProceed = !!(draft.dbServer?.trim() && draft.dbName?.trim());

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 2: Database Credentials</h2>
        <p className="text-sm text-neutral-500 mt-1">
          Provide the SQL Server connection details for this node.
        </p>
      </div>
      <div className="space-y-4">
        <div className="space-y-1">
          <label className="text-sm font-medium">DB Server *</label>
          <input
            className="w-full rounded-md border px-3 py-2 text-sm dark:bg-neutral-900 dark:border-neutral-700"
            placeholder="sql-host\INSTANCE or sql-host,1433"
            value={draft.dbServer ?? ''}
            onChange={e => onChange({ dbServer: e.target.value })}
          />
        </div>
        <div className="space-y-1">
          <label className="text-sm font-medium">Database Name *</label>
          <input
            className="w-full rounded-md border px-3 py-2 text-sm dark:bg-neutral-900 dark:border-neutral-700"
            placeholder="SyncSourceDB"
            value={draft.dbName ?? ''}
            onChange={e => onChange({ dbName: e.target.value })}
          />
        </div>
      </div>
      <div className="flex justify-between">
        <Button variant="outline" onClick={onBack}>Back</Button>
        <Button onClick={onNext} disabled={!canProceed}>Next</Button>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Create Step3Network**

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/steps/Step3Network.tsx
import { useEffect } from 'react';
import { Button } from '../../../../components/ui/button';
import type { ProvisionWizardDraft } from '../../types/provision';

interface Props {
  draft:    ProvisionWizardDraft;
  onChange: (patch: Partial<ProvisionWizardDraft>) => void;
  onNext:   () => void;
  onBack:   () => void;
}

function toExternalId(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

export function Step3Network({ draft, onChange, onNext, onBack }: Props) {
  const canProceed = !!(draft.nodeName?.trim() && draft.externalId?.trim());

  // Auto-generate externalId from nodeName when nodeName changes
  useEffect(() => {
    if (draft.nodeName) {
      onChange({ externalId: toExternalId(draft.nodeName) });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft.nodeName]);

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 3: Network Identity</h2>
        <p className="text-sm text-neutral-500 mt-1">
          Give this node a name and assign it to a group.
        </p>
      </div>
      <div className="space-y-4">
        <div className="space-y-1">
          <label className="text-sm font-medium">Node Name *</label>
          <input
            className="w-full rounded-md border px-3 py-2 text-sm dark:bg-neutral-900 dark:border-neutral-700"
            placeholder="e.g. warehouse-node-01"
            value={draft.nodeName ?? ''}
            onChange={e => onChange({ nodeName: e.target.value })}
          />
        </div>
        <div className="space-y-1">
          <label className="text-sm font-medium">External ID *</label>
          <p className="text-xs text-neutral-400">Auto-generated from name. Edit if needed.</p>
          <input
            className="w-full rounded-md border px-3 py-2 text-sm dark:bg-neutral-900 dark:border-neutral-700"
            value={draft.externalId ?? ''}
            onChange={e => onChange({ externalId: e.target.value })}
          />
        </div>
        <div className="space-y-1">
          <label className="text-sm font-medium">Group ID (optional)</label>
          <input
            className="w-full rounded-md border px-3 py-2 text-sm dark:bg-neutral-900 dark:border-neutral-700"
            placeholder="e.g. warehouse-group"
            value={draft.groupId ?? ''}
            onChange={e => onChange({ groupId: e.target.value || undefined })}
          />
        </div>
      </div>
      <div className="flex justify-between">
        <Button variant="outline" onClick={onBack}>Back</Button>
        <Button onClick={onNext} disabled={!canProceed}>Next</Button>
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Create Step4Review**

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/steps/Step4Review.tsx
import { Button } from '../../../../components/ui/button';
import type { ProvisionWizardDraft } from '../../types/provision';

interface Props {
  draft:      ProvisionWizardDraft;
  onSubmit:   () => void;
  onBack:     () => void;
  isLoading:  boolean;
}

export function Step4Review({ draft, onSubmit, onBack, isLoading }: Props) {
  const rows: { label: string; value: string | undefined }[] = [
    { label: 'Node Type',    value: draft.nodeType },
    { label: 'Description',  value: draft.description },
    { label: 'DB Server',    value: draft.dbServer },
    { label: 'Database',     value: draft.dbName },
    { label: 'Node Name',    value: draft.nodeName },
    { label: 'External ID',  value: draft.externalId },
    { label: 'Group ID',     value: draft.groupId },
  ];

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 4: Review</h2>
        <p className="text-sm text-neutral-500 mt-1">
          Confirm the configuration before provisioning.
        </p>
      </div>
      <div className="rounded-lg border dark:border-neutral-700 overflow-hidden">
        <table className="w-full text-sm">
          <tbody>
            {rows.filter(r => r.value).map(r => (
              <tr key={r.label} className="border-b dark:border-neutral-700 last:border-0">
                <td className="px-4 py-2.5 font-medium text-neutral-500 w-1/3">{r.label}</td>
                <td className="px-4 py-2.5 text-neutral-900 dark:text-neutral-100">{r.value}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="flex justify-between">
        <Button variant="outline" onClick={onBack} disabled={isLoading}>Back</Button>
        <Button onClick={onSubmit} disabled={isLoading}>
          {isLoading ? 'Provisioning…' : 'Provision Node'}
        </Button>
      </div>
    </div>
  );
}
```

- [ ] **Step 7: Create Step5Complete**

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/steps/Step5Complete.tsx
import { useState } from 'react';
import { Button } from '../../../../components/ui/button';
import { useProvisionPackage } from '../../hooks/useProvisionPackage';
import { Eye, EyeOff, Copy, Download } from 'lucide-react';
import { toast } from 'sonner';

interface Props {
  nodeId:    string;
  token:     string | null;   // null when page was refreshed after provision
  onRestart: () => void;
}

export function Step5Complete({ nodeId, token, onRestart }: Props) {
  const [revealed, setRevealed] = useState(false);
  const pkgMutation = useProvisionPackage();

  async function handleCopy() {
    if (!token) return;
    await navigator.clipboard.writeText(token);
    toast.success('Token copied to clipboard');
  }

  async function handleDownload() {
    if (!token) return;
    await pkgMutation.mutateAsync({ nodeId, token });
  }

  if (!token) {
    return (
      <div className="space-y-4">
        <div className="rounded-lg border border-red-300 bg-red-50 dark:bg-red-950/30 p-4">
          <p className="font-medium text-red-700 dark:text-red-300">Token cannot be recovered</p>
          <p className="text-sm text-red-600 dark:text-red-400 mt-1">
            The one-time token was only available at provisioning time. You must re-provision
            this node to generate a new token.
          </p>
        </div>
        <Button variant="outline" onClick={onRestart}>
          Return to Step 1
        </Button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 5: Complete</h2>
        <p className="text-sm text-neutral-500 mt-1">
          Node <strong>{nodeId}</strong> has been provisioned.
        </p>
      </div>

      <div className="rounded-lg border border-amber-300 bg-amber-50 dark:bg-amber-950/30 p-4">
        <p className="font-medium text-amber-700 dark:text-amber-300 text-sm">
          One-time token — save it now
        </p>
        <p className="text-xs text-amber-600 dark:text-amber-400 mt-1">
          This token will not be shown again. If you navigate away before saving it,
          you must re-provision this node.
        </p>
      </div>

      <div className="space-y-2">
        <label className="text-sm font-medium">Registration Token</label>
        <div className="flex items-center gap-2">
          <div className="flex-1 rounded-md border px-3 py-2 text-sm font-mono bg-neutral-50 dark:bg-neutral-900 dark:border-neutral-700 truncate">
            {revealed ? token : '•'.repeat(Math.min(token.length, 32))}
          </div>
          <Button
            size="icon"
            variant="ghost"
            onClick={() => setRevealed(r => !r)}
            title={revealed ? 'Hide token' : 'Reveal token'}
          >
            {revealed ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
          </Button>
          <Button size="icon" variant="ghost" onClick={handleCopy} title="Copy token">
            <Copy className="h-4 w-4" />
          </Button>
        </div>
      </div>

      <Button
        variant="outline"
        onClick={handleDownload}
        disabled={pkgMutation.isPending}
        className="w-full"
      >
        <Download className="h-4 w-4 mr-2" />
        {pkgMutation.isPending ? 'Preparing…' : 'Download Provision Package'}
      </Button>
    </div>
  );
}
```

- [ ] **Step 8: Create ProvisionWizard coordinator**

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/components/ProvisionWizard.tsx
import { useState, useEffect } from 'react';
import { toast } from 'sonner';
import { useNodeManagement } from '../../NodeManagementProvider';
import { Step1NodeType }   from '../steps/Step1NodeType';
import { Step2Credentials } from '../steps/Step2Credentials';
import { Step3Network }    from '../steps/Step3Network';
import { Step4Review }     from '../steps/Step4Review';
import { Step5Complete }   from '../steps/Step5Complete';
import {
  loadWizardDraft, saveWizardDraft, clearWizardDraft,
} from '../../types/provision';
import type { ProvisionWizardDraft } from '../../types/provision';
import { useProvision } from '../../hooks/useProvision';

const EMPTY_DRAFT: ProvisionWizardDraft = { step: 1 };

const STEPS = ['Node Type', 'Credentials', 'Network', 'Review', 'Complete'];

function StepIndicator({ current }: { current: number }) {
  return (
    <div className="flex items-center gap-2 mb-6">
      {STEPS.map((label, i) => {
        const n = i + 1;
        const active = n === current;
        const done   = n < current;
        return (
          <div key={n} className="flex items-center gap-1">
            <div className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-medium ${
              done   ? 'bg-green-600 text-white' :
              active ? 'bg-blue-600 text-white' :
                       'bg-neutral-200 text-neutral-500 dark:bg-neutral-700 dark:text-neutral-400'
            }`}>
              {done ? '✓' : n}
            </div>
            <span className={`text-xs hidden sm:block ${active ? 'font-medium' : 'text-neutral-400'}`}>
              {label}
            </span>
            {i < STEPS.length - 1 && (
              <div className="w-4 h-px bg-neutral-200 dark:bg-neutral-700 mx-1" />
            )}
          </div>
        );
      })}
    </div>
  );
}

export function ProvisionWizard() {
  const { setWizardDraft } = useNodeManagement();
  const provision = useProvision();

  const [step, setStep]               = useState(1);
  const [draft, setDraft]             = useState<ProvisionWizardDraft>(EMPTY_DRAFT);
  const [provisionResult, setResult]  =
    useState<{ nodeId: string; token: string } | null>(null);
  const [draftOffered, setDraftOffered] = useState(false);

  useEffect(() => {
    if (draftOffered) return;
    setDraftOffered(true);
    const saved = loadWizardDraft();
    if (saved) {
      toast('Resume previous draft?', {
        action: {
          label: 'Resume',
          onClick: () => {
            setDraft(saved);
            setStep(saved.step ?? 1);
          },
        },
        cancel: {
          label: 'Start fresh',
          onClick: () => clearWizardDraft(),
        },
        duration: 10_000,
      });
    }
  }, [draftOffered]);

  function patch(partial: Partial<ProvisionWizardDraft>) {
    setDraft(prev => {
      const next = { ...prev, ...partial };
      setWizardDraft(next);
      saveWizardDraft(next);
      return next;
    });
  }

  function advance() {
    const next = step + 1;
    patch({ step: next });
    setStep(next);
  }

  function goBack() {
    const prev = step - 1;
    patch({ step: prev });
    setStep(prev);
  }

  function cancel() {
    clearWizardDraft();
    setDraft(EMPTY_DRAFT);
    setWizardDraft(null);
    setStep(1);
    setResult(null);
  }

  async function handleSubmit() {
    try {
      const result = await provision.mutateAsync({
        nodeName:    draft.nodeName!,
        externalId:  draft.externalId!,
        nodeType:    draft.nodeType!,
        dbServer:    draft.dbServer!,
        dbName:      draft.dbName!,
        groupId:     draft.groupId,
        description: draft.description,
      });
      setResult(result);
      clearWizardDraft();
      setWizardDraft(null);
      setStep(5);
    } catch {
      toast.error('Provisioning failed. Please try again.');
    }
  }

  function restart() {
    setDraft(EMPTY_DRAFT);
    setResult(null);
    setStep(1);
  }

  return (
    <div className="max-w-2xl mx-auto py-8 px-4">
      <div className="flex justify-between items-start mb-4">
        <h1 className="text-lg font-semibold">Provision New Node</h1>
        {step < 5 && (
          <button onClick={cancel} className="text-xs text-neutral-400 hover:text-neutral-600">
            Cancel
          </button>
        )}
      </div>
      <StepIndicator current={step} />
      {step === 1 && (
        <Step1NodeType draft={draft} onChange={patch} onNext={advance} />
      )}
      {step === 2 && (
        <Step2Credentials draft={draft} onChange={patch} onNext={advance} onBack={goBack} />
      )}
      {step === 3 && (
        <Step3Network draft={draft} onChange={patch} onNext={advance} onBack={goBack} />
      )}
      {step === 4 && (
        <Step4Review
          draft={draft}
          onSubmit={handleSubmit}
          onBack={goBack}
          isLoading={provision.isPending}
        />
      )}
      {step === 5 && (
        <Step5Complete
          nodeId={provisionResult?.nodeId ?? ''}
          token={provisionResult?.token ?? null}
          onRestart={restart}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 9: Replace ProvisionTab stub**

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/components/ProvisionTab.tsx
import { ProvisionWizard } from './ProvisionWizard';

export function ProvisionTab() {
  return <ProvisionWizard />;
}
```

- [ ] **Step 10: Verify TypeScript build**

```pwsh
cd src/MSOSync.Frontend
npm run build
```

Expected: Build succeeds with zero TypeScript errors. Fix any TS errors before proceeding.

- [ ] **Step 11: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/features/node-management/hooks/useProvision.ts `
  src/MSOSync.Frontend/src/features/node-management/hooks/useProvisionPackage.ts `
  src/MSOSync.Frontend/src/features/node-management/provision/
git commit -m "feat(12A): provision wizard — 5 steps, sessionStorage draft, token display"
```
