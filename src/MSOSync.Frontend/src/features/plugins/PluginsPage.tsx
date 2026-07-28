import { useState } from 'react';
import { ChevronDown, ChevronRight, Package } from 'lucide-react';
import { PluginStatusBadge } from './PluginStatusBadge';
import { PluginSummaryCard } from './PluginSummaryCard';
import { UpdatesPanel } from './UpdatesPanel';
import { useDisablePlugin, useEnablePlugin, usePlugins } from './hooks';
import type { PluginDto } from './types';
import { Button } from '../../components/ui/button';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { EmptyState } from '../../shared/components/feedback/EmptyState';

export function PluginsPage() {
  const { data: plugins, isLoading, isError, error } = usePlugins();
  const enableMutation  = useEnablePlugin();
  const disableMutation = useDisablePlugin();
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const toggleExpand = (pluginId: string) => {
    setExpanded(prev => {
      const next = new Set(prev);
      if (next.has(pluginId)) next.delete(pluginId);
      else next.add(pluginId);
      return next;
    });
  };

  if (isLoading) return <div className="p-8 text-center text-neutral-500">Loading plugins…</div>;
  if (isError)   return <ErrorState error={error} />;

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">Plugins</h1>
        <p className="text-sm text-neutral-500 mt-1">
          Discovered at startup. Restart required after enable/disable changes.
        </p>
      </div>

      <PluginSummaryCard />

      <UpdatesPanel installedPluginIds={plugins?.map(p => p.pluginId) ?? []} />

      {!plugins?.length ? (
        <EmptyState message="No plugins discovered. Place plugin folders in the plugins/ directory." />
      ) : (
        <div className="rounded-lg border border-neutral-200 dark:border-neutral-700 overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 dark:bg-neutral-800">
              <tr>
                <th className="w-8 px-3 py-3" />
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Name</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Version</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Status</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Compatibility</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Load Time</th>
                <th className="px-4 py-3 text-right font-medium text-neutral-600 dark:text-neutral-400">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-neutral-200 dark:divide-neutral-700">
              {plugins.map(plugin => (
                <PluginRow
                  key={plugin.pluginId}
                  plugin={plugin}
                  expanded={expanded.has(plugin.pluginId)}
                  onToggle={() => toggleExpand(plugin.pluginId)}
                  onEnable={() => enableMutation.mutate({ pluginId: plugin.pluginId, name: plugin.name })}
                  onDisable={() => disableMutation.mutate({ pluginId: plugin.pluginId, name: plugin.name })}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

interface PluginRowProps {
  plugin:   PluginDto;
  expanded: boolean;
  onToggle: () => void;
  onEnable: () => void;
  onDisable: () => void;
}

function PluginRow({ plugin, expanded, onToggle, onEnable, onDisable }: PluginRowProps) {
  const isDisabled = plugin.status === 'Disabled';

  return (
    <>
      <tr className="hover:bg-neutral-50 dark:hover:bg-neutral-800/50">
        <td className="px-3 py-3">
          <button onClick={onToggle} className="text-neutral-400 hover:text-neutral-600">
            {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
          </button>
        </td>
        <td className="px-4 py-3 font-medium text-neutral-900 dark:text-neutral-100">
          <div className="flex items-center gap-2">
            <Package className="h-4 w-4 text-neutral-400" />
            {plugin.name}
          </div>
          <div className="text-xs text-neutral-400 font-mono">{plugin.pluginId}</div>
        </td>
        <td className="px-4 py-3 text-neutral-600 dark:text-neutral-400 font-mono text-xs">
          {plugin.version}
        </td>
        <td className="px-4 py-3">
          <PluginStatusBadge status={plugin.status} />
        </td>
        <td className="px-4 py-3 text-neutral-600 dark:text-neutral-400 text-xs">
          {plugin.hostCompatibility}
        </td>
        <td className="px-4 py-3 text-neutral-600 dark:text-neutral-400 text-xs">
          {plugin.loadDurationMs}ms
        </td>
        <td className="px-4 py-3 text-right">
          {isDisabled ? (
            <Button size="sm" variant="outline" onClick={onEnable}>Enable</Button>
          ) : (
            <Button size="sm" variant="outline" onClick={onDisable}>Disable</Button>
          )}
        </td>
      </tr>
      {expanded && (
        <tr className="bg-neutral-50/50 dark:bg-neutral-800/30">
          <td colSpan={7} className="px-8 py-4">
            <PluginExpandedDetail plugin={plugin} />
          </td>
        </tr>
      )}
    </>
  );
}

function PluginExpandedDetail({ plugin }: { plugin: PluginDto }) {
  return (
    <div className="space-y-3 text-sm">
      {plugin.lastError && (
        <div className="rounded-md bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 p-3">
          <div className="text-xs font-medium text-red-700 dark:text-red-400 mb-1">
            Failed at stage: {plugin.failureStage}
          </div>
          <div className="text-red-800 dark:text-red-300 font-mono text-xs">{plugin.lastError}</div>
        </div>
      )}
      <div className="grid grid-cols-2 gap-4 text-xs text-neutral-600 dark:text-neutral-400">
        {plugin.dependencies.length > 0 && (
          <div>
            <span className="font-medium">Dependencies:</span>{' '}
            {plugin.dependencies.join(', ')}
          </div>
        )}
        {plugin.capabilities.length > 0 && (
          <div>
            <span className="font-medium">Capabilities:</span>{' '}
            {plugin.capabilities.join(', ')}
          </div>
        )}
        {plugin.permissions.length > 0 && (
          <div>
            <span className="font-medium">Permissions:</span>{' '}
            {plugin.permissions.join(', ')}
          </div>
        )}
        <div>
          <span className="font-medium">Loaded at:</span>{' '}
          {new Date(plugin.loadedAt).toLocaleString()}
        </div>
      </div>
    </div>
  );
}
