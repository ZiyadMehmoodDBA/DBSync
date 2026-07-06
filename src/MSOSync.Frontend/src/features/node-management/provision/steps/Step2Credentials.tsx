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
