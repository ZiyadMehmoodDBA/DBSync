import { useEffect, useRef } from 'react';
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
  const externalIdEdited = useRef(false);
  const canProceed = !!(draft.nodeName?.trim() && draft.externalId?.trim());

  useEffect(() => {
    if (externalIdEdited.current) return;
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
            onChange={e => {
              externalIdEdited.current = true;
              onChange({ externalId: e.target.value });
            }}
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
