import { ConfigurationStateBadge } from '../../components/ui/ConfigurationStateBadge';
import { EffectiveConfigPreview } from '../../components/ui/EffectiveConfigPreview';
import { formatRelativeTime } from '../../shared/utils/date';
import { useNodeConfiguration, useNodeConfigurationHistory } from './hooks';

interface Props {
  nodeId: string;
}

export function NodeConfigurationTab({ nodeId }: Props) {
  const { data: config, isLoading, error } = useNodeConfiguration(nodeId);
  const { data: history = [] } = useNodeConfigurationHistory(nodeId);

  if (isLoading) return <p className="p-4 text-sm text-neutral-500">Loading…</p>;
  if (error) return <p className="p-4 text-sm text-red-500">Failed to load configuration.</p>;

  return (
    <div className="flex flex-col gap-4 p-4 text-sm">
      <div className="flex items-center gap-2">
        <span className="text-neutral-500">State:</span>
        <ConfigurationStateBadge state={config?.configurationState} />
      </div>

      {config?.assignedTemplateId ? (
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-neutral-500 uppercase tracking-wide">
            Assignment
          </span>
          <div className="text-xs text-neutral-700 dark:text-neutral-300">
            v{config.assignedTemplateVersion}
            {config.appliedTemplateVersion != null
              ? ` · Applied v${config.appliedTemplateVersion}`
              : ' · Not yet applied'}
          </div>
          {config.lastAppliedAt && (
            <div className="text-xs text-neutral-500">
              Last applied {formatRelativeTime(config.lastAppliedAt)}
            </div>
          )}
        </div>
      ) : (
        <p className="text-xs text-neutral-500">No template assigned.</p>
      )}

      {config?.effectiveSettings && (
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-neutral-500 uppercase tracking-wide">
            Effective Config
          </span>
          <EffectiveConfigPreview
            settings={config.effectiveSettings}
            overrides={config.overrides}
          />
        </div>
      )}

      {history.length > 0 && (
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-neutral-500 uppercase tracking-wide">
            Recent History
          </span>
          <div className="divide-y">
            {history.slice(0, 5).map((h) => (
              <div key={h.id} className="py-1.5">
                <div className="font-medium text-xs">{h.eventType}</div>
                <div className="text-xs text-neutral-500">
                  {formatRelativeTime(h.occurredAt)}
                  {h.templateVersion != null ? ` · v${h.templateVersion}` : ''}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
