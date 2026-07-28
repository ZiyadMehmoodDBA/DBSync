import { useState } from 'react';
import { RefreshCw, Loader2, ArrowRight } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Separator } from '../../components/ui/separator';
import { EmptyState } from '../../shared/components/feedback/EmptyState';
import { useCheckAllUpdates, useUpdatePlugin } from '../../shared/hooks/useMarketplace';

interface UpdatesPanelProps {
  installedPluginIds: string[];   // from usePlugins() in PluginsPage — passed as prop, not re-fetched
}

export function UpdatesPanel({ installedPluginIds: _installedPluginIds }: UpdatesPanelProps) {
  // installedPluginIds is accepted for future filtering if the parent page expands.
  // The backend bulk-check covers all installed plugins server-side, so it's not
  // used to filter results here — but the prop is kept for consistency with the spec.
  const { data, isLoading, isMarketplaceUnconfigured, refetch } = useCheckAllUpdates();
  const updateMutation = useUpdatePlugin();
  const [inFlight, setInFlight] = useState<Set<string>>(new Set());

  const updates = data?.updates ?? [];

  async function handleUpdateAll() {
    for (const manifest of updates) {
      setInFlight(prev => new Set(prev).add(manifest.pluginId));
      try {
        await updateMutation.mutateAsync({
          id:      manifest.pluginId,
          version: manifest.availableVersion,
          name:    manifest.pluginId,
        });
      } finally {
        setInFlight(prev => {
          const next = new Set(prev);
          next.delete(manifest.pluginId);
          return next;
        });
      }
    }
  }

  async function handleUpdateOne(pluginId: string, availableVersion: string) {
    setInFlight(prev => new Set(prev).add(pluginId));
    try {
      await updateMutation.mutateAsync({
        id:      pluginId,
        version: availableVersion,
        name:    pluginId,
      });
    } finally {
      setInFlight(prev => {
        const next = new Set(prev);
        next.delete(pluginId);
        return next;
      });
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold text-neutral-900 dark:text-neutral-100">
            Plugin Updates
          </h2>
          <p className="text-sm text-neutral-500">
            Check for available updates to installed plugins.
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={refetch}
          disabled={isLoading}
          className="gap-2"
        >
          <RefreshCw className={`h-4 w-4 ${isLoading ? 'animate-spin' : ''}`} />
          Check for Updates
        </Button>
      </div>

      <Separator />

      {/* Loading */}
      {isLoading && (
        <div className="flex items-center justify-center py-8 gap-2 text-sm text-neutral-500">
          <Loader2 className="h-4 w-4 animate-spin" />
          Checking for updates…
        </div>
      )}

      {/* Unconfigured */}
      {!isLoading && isMarketplaceUnconfigured && (
        <EmptyState message="Marketplace not configured. Contact your administrator to set up the plugin registry." />
      )}

      {/* No updates */}
      {!isLoading && !isMarketplaceUnconfigured && updates.length === 0 && (
        <EmptyState message="All plugins are up to date." />
      )}

      {/* Updates list */}
      {!isLoading && !isMarketplaceUnconfigured && updates.length > 0 && (
        <div className="space-y-3">
          <div className="flex justify-end">
            <Button
              size="sm"
              onClick={() => void handleUpdateAll()}
              disabled={inFlight.size > 0}
            >
              Update All ({updates.length})
            </Button>
          </div>

          <div className="rounded-lg border border-neutral-200 dark:border-neutral-700 divide-y divide-neutral-200 dark:divide-neutral-700">
            {updates.map(manifest => (
              <div
                key={manifest.pluginId}
                className="flex items-start gap-4 px-4 py-3"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-neutral-900 dark:text-neutral-100">
                    {manifest.pluginId}
                  </p>
                  <div className="flex items-center gap-1.5 mt-0.5 text-xs text-neutral-500">
                    <span className="font-mono">v{manifest.installedVersion}</span>
                    <ArrowRight className="h-3 w-3 shrink-0" />
                    <span className="font-mono text-green-600 dark:text-green-400">
                      v{manifest.availableVersion}
                    </span>
                  </div>
                  {manifest.releaseNotes && (
                    <p className="mt-1 text-xs text-neutral-500 truncate max-w-md">
                      {manifest.releaseNotes.slice(0, 100)}
                      {manifest.releaseNotes.length > 100 ? '…' : ''}
                    </p>
                  )}
                </div>

                <Button
                  size="sm"
                  variant="outline"
                  className="shrink-0 gap-1.5"
                  disabled={inFlight.has(manifest.pluginId)}
                  onClick={() => void handleUpdateOne(manifest.pluginId, manifest.availableVersion)}
                  aria-label={`Update ${manifest.pluginId} to ${manifest.availableVersion}`}
                >
                  {inFlight.has(manifest.pluginId) ? (
                    <Loader2 className="h-3 w-3 animate-spin" />
                  ) : null}
                  Update
                </Button>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
