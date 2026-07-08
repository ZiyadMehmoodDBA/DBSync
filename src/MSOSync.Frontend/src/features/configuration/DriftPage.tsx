import { useState } from 'react';
import { ConfigurationStateBadge } from '../../components/ui/ConfigurationStateBadge';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import { useConfigurationSummary, useDriftNodes } from './hooks';
import { useStartRollout } from './mutations';
import { useTemplates } from './hooks';
import { TemplateVersionSelect } from '../../components/ui/TemplateVersionSelect';
import type { ConfigurationState, DriftNodeDto } from './types';

const DRIFT_STATES: { key: ConfigurationState; label: string; color: string }[] = [
  { key: 'Current',         label: 'Current',          color: 'text-green-600' },
  { key: 'UpdateAvailable', label: 'Update Available',  color: 'text-yellow-600' },
  { key: 'Applying',        label: 'Applying',          color: 'text-blue-600' },
  { key: 'Drifted',         label: 'Drifted',           color: 'text-orange-600' },
  { key: 'Failed',          label: 'Failed',            color: 'text-red-600' },
  { key: 'None',            label: 'Unassigned',        color: 'text-gray-500' },
];

export function DriftPage() {
  const [stateFilter, setStateFilter] = useState<string>('');
  const [search, setSearch] = useState('');
  const [selectedNodes, setSelectedNodes] = useState<Set<string>>(new Set());
  const [rolloutForm, setRolloutForm] = useState<{ templateId: string; version: number | null }>({
    templateId: '', version: null,
  });

  const { data: summary } = useConfigurationSummary();
  const { data: nodes = [], isLoading } = useDriftNodes({
    state: stateFilter || undefined,
    search: search || undefined,
  });
  const { data: templates = [] } = useTemplates('Published');
  const rolloutMutation = useStartRollout();

  const toggleNode = (nodeId: string) => {
    setSelectedNodes((prev) => {
      const next = new Set(prev);
      if (next.has(nodeId)) next.delete(nodeId); else next.add(nodeId);
      return next;
    });
  };

  const handleRollout = async () => {
    if (!rolloutForm.templateId || !rolloutForm.version || selectedNodes.size === 0) return;
    try {
      await rolloutMutation.mutateAsync({
        templateId: rolloutForm.templateId,
        templateVersion: rolloutForm.version,
        nodeIds: [...selectedNodes],
      });
      toast.success(`Rollout started for ${selectedNodes.size} nodes`);
      setSelectedNodes(new Set());
      setRolloutForm({ templateId: '', version: null });
    } catch (e) {
      toast.error(getErrorMessage(e));
    }
  };

  const SUMMARY_MAP: Record<ConfigurationState, keyof typeof summary> = {
    Current:         'currentCount',
    UpdateAvailable: 'updateAvailableCount',
    Applying:        'applyingCount',
    Drifted:         'driftedCount',
    Failed:          'failedCount',
    None:            'noneCount',
    Unknown:         'unknownCount',
  } as Record<ConfigurationState, keyof typeof summary>;

  return (
    <div className="flex flex-col gap-6 p-6">
      <h1 className="text-2xl font-semibold">Configuration Drift</h1>

      {summary && (
        <div className="grid grid-cols-3 gap-3 md:grid-cols-6">
          {DRIFT_STATES.map(({ key, label, color }) => (
            <button
              key={key}
              type="button"
              onClick={() => setStateFilter(stateFilter === key ? '' : key)}
              className={`rounded-lg border p-3 text-left transition-colors ${
                stateFilter === key ? 'bg-neutral-100 dark:bg-neutral-800' : 'hover:bg-neutral-50'
              }`}
            >
              <div className={`text-xl font-bold ${color}`}>
                {summary[SUMMARY_MAP[key] as keyof typeof summary] as number ?? 0}
              </div>
              <div className="text-xs text-neutral-500">{label}</div>
            </button>
          ))}
        </div>
      )}

      <div className="flex items-center gap-3 flex-wrap">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search nodes…"
          className="max-w-xs"
        />
        {selectedNodes.size > 0 && (
          <div className="flex items-center gap-2 rounded-lg border px-3 py-2">
            <span className="text-sm">{selectedNodes.size} selected</span>
            <select
              className="rounded border px-2 py-1 text-sm"
              value={rolloutForm.templateId}
              onChange={(e) => setRolloutForm({ templateId: e.target.value, version: null })}
            >
              <option value="">Template…</option>
              {templates.map((t) => (
                <option key={t.id} value={t.id}>{t.name}</option>
              ))}
            </select>
            {rolloutForm.templateId && (
              <TemplateVersionSelect
                templateId={rolloutForm.templateId}
                value={rolloutForm.version}
                onChange={(v) => setRolloutForm((f) => ({ ...f, version: v }))}
              />
            )}
            <Button
              size="sm"
              disabled={!rolloutForm.templateId || !rolloutForm.version || rolloutMutation.isPending}
              onClick={() => void handleRollout()}
            >
              Rollout
            </Button>
          </div>
        )}
      </div>

      {isLoading ? (
        <p className="text-sm text-neutral-500">Loading…</p>
      ) : nodes.length === 0 ? (
        <p className="text-sm text-neutral-500">No nodes match the filter.</p>
      ) : (
        <div className="divide-y rounded-lg border">
          {nodes.map((n: DriftNodeDto) => (
            <div
              key={n.nodeId}
              className="flex items-center gap-3 px-4 py-3 cursor-pointer hover:bg-neutral-50 dark:hover:bg-neutral-800"
              onClick={() => toggleNode(n.nodeId)}
            >
              <input
                type="checkbox"
                checked={selectedNodes.has(n.nodeId)}
                onChange={() => toggleNode(n.nodeId)}
                onClick={(e) => e.stopPropagation()}
              />
              <div className="flex flex-1 flex-col gap-0.5">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-sm">{n.nodeName}</span>
                  <ConfigurationStateBadge state={n.configurationState} />
                </div>
                <span className="text-xs text-neutral-500">
                  {n.assignedTemplateName
                    ? `Assigned: ${n.assignedTemplateName} v${n.assignedTemplateVersion}`
                    : 'No template assigned'}
                  {n.appliedTemplateVersion != null
                    ? ` · Applied: v${n.appliedTemplateVersion}`
                    : ''}
                </span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
