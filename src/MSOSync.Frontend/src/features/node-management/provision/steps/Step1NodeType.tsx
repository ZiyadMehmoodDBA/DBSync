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
