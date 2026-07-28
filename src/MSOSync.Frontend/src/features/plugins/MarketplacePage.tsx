import { useState, useEffect, useCallback } from 'react';
import { Search } from 'lucide-react';
import { Button } from '../../components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { EmptyState } from '../../shared/components/feedback/EmptyState';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { useMarketplaceSearch, useInstallPlugin } from '../../shared/hooks/useMarketplace';
import { usePlugins } from './hooks';
import { MarketplacePluginCard } from './MarketplacePluginCard';
import { MarketplacePluginDrawer } from './MarketplacePluginDrawer';
import {
  MARKETPLACE_CATEGORIES,
  type MarketplaceSortOrder,
  type MarketplacePluginListItemDto,
} from '../../shared/types/marketplace';

const PAGE_SIZE = 20;

function sortPlugins(
  plugins: MarketplacePluginListItemDto[],
  sort: MarketplaceSortOrder,
): MarketplacePluginListItemDto[] {
  const copy = [...plugins];
  if (sort === 'newest') {
    return copy.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  }
  if (sort === 'popular') {
    return copy.sort((a, b) => b.downloadCount - a.downloadCount);
  }
  // 'rating'
  return copy.sort((a, b) => b.rating - a.rating);
}

export function MarketplacePage() {
  const [rawQuery,  setRawQuery]  = useState('');
  const [query,     setQuery]     = useState('');      // debounced
  const [category,  setCategory]  = useState('All');
  const [sort,      setSort]      = useState<MarketplaceSortOrder>('newest');
  const [page,      setPage]      = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Debounce search input 300 ms
  useEffect(() => {
    const timer = setTimeout(() => {
      setQuery(rawQuery);
      setPage(1);   // reset to page 1 on new search
    }, 300);
    return () => clearTimeout(timer);
  }, [rawQuery]);

  // Reset page on category change
  const handleCategoryChange = useCallback((value: string) => {
    setCategory(value);
    setPage(1);
  }, []);

  const { data, isLoading, isError, isMarketplaceUnconfigured, error, refetch } =
    useMarketplaceSearch({ query, category, page, pageSize: PAGE_SIZE });

  const installMutation = useInstallPlugin();
  const { data: installedPlugins } = usePlugins();

  const installedIdSet = new Set(installedPlugins?.map(p => p.pluginId) ?? []);

  const sortedPlugins = data ? sortPlugins(data.data, sort) : [];

  const handleInstall = useCallback(
    (id: string, name: string) => {
      installMutation.mutate({ id, name });
    },
    [installMutation],
  );

  const handleDrawerInstall = useCallback(
    (id: string, version: string, name: string) => {
      installMutation.mutate({ id, version, name });
    },
    [installMutation],
  );

  return (
    <div className="p-6 space-y-6">
      {/* Page header */}
      <div>
        <h1 className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">Marketplace</h1>
        <p className="text-sm text-neutral-500 mt-1">
          Discover and install plugins from the MSOSync plugin registry.
        </p>
      </div>

      {/* Toolbar */}
      <div className="flex items-center gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-neutral-400" />
          <input
            type="text"
            value={rawQuery}
            onChange={(e) => setRawQuery(e.target.value)}
            placeholder="Search plugins…"
            aria-label="Search plugins"
            className="w-full rounded-md border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 pl-9 pr-3 py-2 text-sm placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-300 dark:focus:ring-neutral-600"
          />
        </div>

        <Select value={category} onValueChange={handleCategoryChange}>
          <SelectTrigger className="w-44" aria-label="Filter by category">
            <SelectValue placeholder="Category" />
          </SelectTrigger>
          <SelectContent>
            {MARKETPLACE_CATEGORIES.map(c => (
              <SelectItem key={c} value={c}>{c}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select value={sort} onValueChange={(v) => setSort(v as MarketplaceSortOrder)}>
          <SelectTrigger className="w-44">
            <SelectValue placeholder="Sort by" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="newest">Newest</SelectItem>
            <SelectItem value="popular">Most Downloaded</SelectItem>
            <SelectItem value="rating">Top Rated</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* States */}
      {isLoading && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4" aria-busy="true">
          {Array.from({ length: 12 }, (_, i) => (
            <div
              key={i}
              className="rounded-lg border border-neutral-200 dark:border-neutral-700 bg-neutral-50 dark:bg-neutral-800 h-44 animate-pulse"
            />
          ))}
        </div>
      )}

      {!isLoading && isMarketplaceUnconfigured && (
        <EmptyState message="Marketplace not configured. Contact your administrator to set up the plugin registry." />
      )}

      {!isLoading && isError && !isMarketplaceUnconfigured && (
        <ErrorState error={error} onRetry={refetch} />
      )}

      {!isLoading && !isMarketplaceUnconfigured && !isError && data && (
        <>
          {sortedPlugins.length === 0 ? (
            <EmptyState message="No plugins found. Try adjusting your search or filters." />
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {sortedPlugins.map(plugin => (
                <MarketplacePluginCard
                  key={plugin.id}
                  plugin={plugin}
                  isInstalled={installedIdSet.has(plugin.id)}
                  onSelect={setSelectedId}
                  onInstall={handleInstall}
                  isInstalling={
                    installMutation.isPending &&
                    installMutation.variables?.id === plugin.id
                  }
                />
              ))}
            </div>
          )}

          {/* Pagination */}
          {data.totalPages > 1 && (
            <div className="flex items-center justify-center gap-3 pt-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
              >
                Previous
              </Button>
              <span className="text-sm text-neutral-600 dark:text-neutral-400">
                Page {data.page} of {data.totalPages}
              </span>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPage(p => Math.min(data.totalPages, p + 1))}
                disabled={page === data.totalPages}
              >
                Next
              </Button>
            </div>
          )}
        </>
      )}

      {/* Detail drawer */}
      <MarketplacePluginDrawer
        pluginId={selectedId}
        onClose={() => setSelectedId(null)}
        isInstalled={selectedId !== null && installedIdSet.has(selectedId)}
        onInstall={handleDrawerInstall}
        isInstalling={
          installMutation.isPending &&
          installMutation.variables?.id === selectedId
        }
      />
    </div>
  );
}
