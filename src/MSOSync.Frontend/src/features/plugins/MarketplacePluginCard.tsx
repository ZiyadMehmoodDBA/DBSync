import { useState } from 'react';
import { Package, ShieldCheck, Loader2 } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Badge } from '../../components/ui/badge';
import { MarketplaceStarRating } from './MarketplaceStarRating';
import type { MarketplacePluginListItemDto } from '../../shared/types/marketplace';

interface MarketplacePluginCardProps {
  plugin:       MarketplacePluginListItemDto;
  isInstalled:  boolean;
  onSelect:     (id: string) => void;
  onInstall:    (id: string, name: string) => void;
  isInstalling: boolean;
}

export function MarketplacePluginCard({
  plugin,
  isInstalled,
  onSelect,
  onInstall,
  isInstalling,
}: MarketplacePluginCardProps) {
  const [iconError, setIconError] = useState(false);
  const showIcon = plugin.iconUrl && !iconError;

  const downloadLabel = new Intl.NumberFormat('en-US', {
    notation: 'compact',
    maximumFractionDigits: 1,
  }).format(plugin.downloadCount);

  return (
    <div
      className="relative flex flex-col gap-3 rounded-lg border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 p-4 hover:border-neutral-300 dark:hover:border-neutral-600 transition-colors cursor-pointer"
      onClick={() => onSelect(plugin.id)}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') onSelect(plugin.id); }}
    >
      {/* Header row */}
      <div className="flex items-start gap-3">
        <div className="shrink-0">
          {showIcon ? (
            <img
              src={plugin.iconUrl!}
              alt={`${plugin.name} icon`}
              className="h-8 w-8 rounded"
              onError={() => setIconError(true)}
            />
          ) : (
            <Package className="h-8 w-8 text-neutral-400" />
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <span className="font-medium text-neutral-900 dark:text-neutral-100 truncate">
              {plugin.name}
            </span>
            {plugin.verified && (
              <ShieldCheck
                className="h-4 w-4 shrink-0 text-blue-500"
                aria-label="Verified publisher"
              />
            )}
          </div>
          <p className="text-xs text-neutral-500 dark:text-neutral-400">{plugin.author}</p>
        </div>
        <span className="rounded-full px-2 py-0.5 text-xs bg-neutral-100 dark:bg-neutral-800 text-neutral-600 dark:text-neutral-300 shrink-0">
          {plugin.category}
        </span>
      </div>

      {/* Description */}
      <p className="line-clamp-2 text-sm text-neutral-600 dark:text-neutral-400">
        {plugin.description}
      </p>

      {/* Footer row */}
      <div className="flex items-center justify-between mt-auto">
        <div className="flex items-center gap-3">
          <MarketplaceStarRating rating={plugin.rating} ratingCount={plugin.ratingCount} />
          <span className="text-xs text-neutral-500">{downloadLabel} downloads</span>
          <span className="text-xs text-neutral-400 font-mono">v{plugin.latestVersion}</span>
        </div>

        {/* Action — stop click propagation so card body click doesn't also fire */}
        <div onClick={(e) => e.stopPropagation()}>
          {isInstalled ? (
            <Badge variant="secondary" className="bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400">
              Installed
            </Badge>
          ) : (
            <Button
              size="sm"
              disabled={isInstalling}
              onClick={() => onInstall(plugin.id, plugin.name)}
              aria-label={`Install ${plugin.name}`}
            >
              {isInstalling ? (
                <Loader2 className="h-3 w-3 animate-spin mr-1" />
              ) : null}
              Install
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}
