// src/MSOSync.Frontend/src/features/node-management/provision/steps/Step4SyncScope.tsx
import { useId } from 'react';
import { useChannels } from '../../../channels/hooks';
import { useTriggers } from '../../../triggers/hooks';
import { useRouters }  from '../../../routers/hooks';
import { Button }      from '../../../../../components/ui/button';
import { Link }        from 'react-router-dom';
import type { ProvisionWizardDraft } from '../../types/provision';
import type { SyncDirection, InitialLoadPolicy } from '../../../../../shared/types';

interface Props {
  draft:    ProvisionWizardDraft;
  onChange: (patch: Partial<ProvisionWizardDraft>) => void;
  onNext:   () => void;
  onBack:   () => void;
}

function toggle(ids: string[], id: string): string[] {
  return ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id];
}

export function Step4SyncScope({ draft, onChange, onNext, onBack }: Props) {
  const scopeId = useId();
  const { data: channels = [], isLoading: loadingChannels } = useChannels();
  const { data: triggers = [], isLoading: loadingTriggers } = useTriggers();
  const { data: routers  = [], isLoading: loadingRouters  } = useRouters();

  const channelIds        = draft.channelIds        ?? [];
  const triggerIds        = draft.triggerIds        ?? [];
  const routerIds         = draft.routerIds         ?? [];
  const syncDirection     = draft.syncDirection     ?? 'Bidirectional';
  const initialLoadPolicy = draft.initialLoadPolicy ?? 'None';

  // Filter triggers by selected channels
  const visibleTriggers = channelIds.length > 0
    ? triggers.filter(t => channelIds.includes(t.channelId))
    : triggers;

  const isLoading = loadingChannels || loadingTriggers || loadingRouters;

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 4: Synchronization Scope</h2>
        <p className="text-sm text-neutral-500 mt-1">
          Assign channels, triggers, and routers to this node. You can skip and configure later.
        </p>
      </div>

      {isLoading ? (
        <p className="text-sm text-neutral-400">Loading topology objects…</p>
      ) : (
        <>
          {/* Channels */}
          <section className="space-y-2">
            <h3 className="text-sm font-medium">Channels</h3>
            {channels.length === 0 ? (
              <p className="text-xs text-neutral-400">No channels defined.</p>
            ) : (
              <div className="space-y-1 max-h-40 overflow-y-auto rounded border dark:border-neutral-700 p-2">
                {channels.map(ch => (
                  <label key={ch.channelId} className="flex items-center gap-2 text-sm cursor-pointer">
                    <input
                      type="checkbox"
                      checked={channelIds.includes(ch.channelId)}
                      onChange={() => {
                        const nextChannelIds = toggle(channelIds, ch.channelId);
                        const validTriggerIds = triggers
                          .filter(t => nextChannelIds.includes(t.channelId))
                          .map(t => t.triggerId);
                        onChange({
                          channelIds: nextChannelIds,
                          triggerIds: triggerIds.filter(id => validTriggerIds.includes(id)),
                        });
                      }}
                    />
                    <span>{ch.name ?? ch.channelId}</span>
                    {!ch.enabled && <span className="text-xs text-neutral-400">(disabled)</span>}
                  </label>
                ))}
              </div>
            )}
          </section>

          {/* Triggers (tables) */}
          <section className="space-y-2">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-medium">
                Tables (Triggers)
                {channelIds.length > 0 && (
                  <span className="text-xs text-neutral-400 ml-2">filtered to selected channels</span>
                )}
              </h3>
              <Link
                to="/triggers"
                className="text-xs text-blue-500 hover:underline"
                target="_blank"
                rel="noopener noreferrer"
              >
                Manage Triggers →
              </Link>
            </div>
            {visibleTriggers.length === 0 ? (
              <p className="text-xs text-neutral-400">
                {triggers.length === 0
                  ? 'No triggers defined. Use Manage Triggers to create them first.'
                  : 'No triggers match the selected channels.'}
              </p>
            ) : (
              <div className="space-y-1 max-h-48 overflow-y-auto rounded border dark:border-neutral-700 p-2">
                {visibleTriggers.map(t => (
                  <label key={t.triggerId} className="flex items-center gap-2 text-sm cursor-pointer">
                    <input
                      type="checkbox"
                      checked={triggerIds.includes(t.triggerId)}
                      onChange={() => onChange({ triggerIds: toggle(triggerIds, t.triggerId) })}
                    />
                    <span className="font-mono text-xs">
                      {t.schemaName}.{t.tableName}
                    </span>
                    <span className="text-xs text-neutral-400">({t.channelId})</span>
                    {!t.enabled && <span className="text-xs text-neutral-400">(disabled)</span>}
                  </label>
                ))}
              </div>
            )}
          </section>

          {/* Routers */}
          <section className="space-y-2">
            <h3 className="text-sm font-medium">Routers</h3>
            {routers.length === 0 ? (
              <p className="text-xs text-neutral-400">No routers defined.</p>
            ) : (
              <div className="space-y-1 max-h-40 overflow-y-auto rounded border dark:border-neutral-700 p-2">
                {routers.map(r => (
                  <label key={r.routerId} className="flex items-center gap-2 text-sm cursor-pointer">
                    <input
                      type="checkbox"
                      checked={routerIds.includes(r.routerId)}
                      onChange={() => onChange({ routerIds: toggle(routerIds, r.routerId) })}
                    />
                    <span>{r.name ?? r.routerId}</span>
                    <span className="text-xs text-neutral-400">
                      {r.sourceGroupId} → {r.targetGroupId}
                    </span>
                  </label>
                ))}
              </div>
            )}
          </section>

          {/* Sync Direction */}
          <section className="space-y-2">
            <h3 className="text-sm font-medium">Sync Direction</h3>
            <div className="space-y-1">
              {(['NodeToHub', 'HubToNode', 'Bidirectional'] as SyncDirection[]).map(dir => (
                <label key={dir} className="flex items-center gap-2 text-sm cursor-pointer">
                  <input
                    type="radio"
                    name={`${scopeId}-syncDirection`}
                    value={dir}
                    checked={syncDirection === dir}
                    onChange={() => onChange({ syncDirection: dir })}
                  />
                  <span>
                    {dir === 'NodeToHub'    && 'Node → Hub (source only)'}
                    {dir === 'HubToNode'    && 'Hub → Node (target only)'}
                    {dir === 'Bidirectional' && 'Bidirectional (source and target)'}
                  </span>
                </label>
              ))}
            </div>
          </section>

          {/* Initial Load Policy */}
          <section className="space-y-2">
            <h3 className="text-sm font-medium">Initial Sync</h3>
            <div className="space-y-1">
              {(['None', 'ChangesOnly', 'FullLoad'] as InitialLoadPolicy[]).map(policy => (
                <label key={policy} className="flex items-center gap-2 text-sm cursor-pointer">
                  <input
                    type="radio"
                    name={`${scopeId}-initialLoadPolicy`}
                    value={policy}
                    checked={initialLoadPolicy === policy}
                    onChange={() => onChange({ initialLoadPolicy: policy })}
                  />
                  <span>
                    {policy === 'None'         && 'None (manual trigger later)'}
                    {policy === 'ChangesOnly'  && 'Changes Only (from activation)'}
                    {policy === 'FullLoad'     && 'Full Load (snapshot all selected tables)'}
                  </span>
                </label>
              ))}
            </div>
          </section>
        </>
      )}

      <div className="flex justify-between">
        <Button variant="outline" onClick={onBack}>Back</Button>
        <div className="flex gap-2">
          <Button variant="ghost" onClick={onNext}>Skip for now</Button>
          <Button onClick={onNext}>Next</Button>
        </div>
      </div>
    </div>
  );
}
