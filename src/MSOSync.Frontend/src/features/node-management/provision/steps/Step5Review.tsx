// src/MSOSync.Frontend/src/features/node-management/provision/steps/Step5Review.tsx
import { Button } from '../../../../components/ui/button';
import type { ProvisionWizardDraft } from '../../types/provision';

interface Props {
  draft:     ProvisionWizardDraft;
  onSubmit:  () => void;
  onBack:    () => void;
  isLoading: boolean;
}

export function Step5Review({ draft, onSubmit, onBack, isLoading }: Props) {
  const rows: { label: string; value: string | undefined }[] = [
    { label: 'Node Type',    value: draft.nodeType },
    { label: 'Description',  value: draft.description },
    { label: 'DB Server',    value: draft.dbServer },
    { label: 'Database',     value: draft.dbName },
    { label: 'Node Name',    value: draft.nodeName },
    { label: 'External ID',  value: draft.externalId },
    { label: 'Group ID',     value: draft.groupId },
  ];

  const hasScope = (draft.channelIds?.length ?? 0) > 0
    || (draft.triggerIds?.length ?? 0) > 0
    || (draft.routerIds?.length ?? 0) > 0
    || draft.syncDirection != null;

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 5: Review</h2>
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

      {hasScope && (
        <div className="rounded-lg border dark:border-neutral-700 overflow-hidden">
          <div className="px-4 py-2 bg-neutral-50 dark:bg-neutral-800 text-xs font-semibold text-neutral-500 uppercase tracking-wide">
            Sync Scope
          </div>
          <table className="w-full text-sm">
            <tbody>
              {(draft.channelIds?.length ?? 0) > 0 && (
                <tr className="border-b dark:border-neutral-700">
                  <td className="px-4 py-2.5 font-medium text-neutral-500 w-1/3">Channels</td>
                  <td className="px-4 py-2.5">{draft.channelIds!.join(', ')}</td>
                </tr>
              )}
              {(draft.triggerIds?.length ?? 0) > 0 && (
                <tr className="border-b dark:border-neutral-700">
                  <td className="px-4 py-2.5 font-medium text-neutral-500 w-1/3">Triggers</td>
                  <td className="px-4 py-2.5">{draft.triggerIds!.join(', ')}</td>
                </tr>
              )}
              {(draft.routerIds?.length ?? 0) > 0 && (
                <tr className="border-b dark:border-neutral-700">
                  <td className="px-4 py-2.5 font-medium text-neutral-500 w-1/3">Routers</td>
                  <td className="px-4 py-2.5">{draft.routerIds!.join(', ')}</td>
                </tr>
              )}
              {draft.syncDirection && (
                <tr className="border-b dark:border-neutral-700 last:border-0">
                  <td className="px-4 py-2.5 font-medium text-neutral-500 w-1/3">Direction</td>
                  <td className="px-4 py-2.5">{draft.syncDirection}</td>
                </tr>
              )}
              {draft.initialLoadPolicy && (
                <tr className="last:border-0">
                  <td className="px-4 py-2.5 font-medium text-neutral-500 w-1/3">Initial Load</td>
                  <td className="px-4 py-2.5">{draft.initialLoadPolicy}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex justify-between">
        <Button variant="outline" onClick={onBack} disabled={isLoading}>Back</Button>
        <Button onClick={onSubmit} disabled={isLoading}>
          {isLoading ? 'Provisioning…' : 'Provision Node'}
        </Button>
      </div>
    </div>
  );
}
