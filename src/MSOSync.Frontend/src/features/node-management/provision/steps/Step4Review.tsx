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
