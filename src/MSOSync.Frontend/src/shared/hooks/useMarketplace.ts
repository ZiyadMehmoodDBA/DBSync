import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type { UseMutationResult } from '@tanstack/react-query';
import {
  searchMarketplace,
  getMarketplacePlugin,
  installMarketplacePlugin,
  checkAllUpdates,
} from '../api/marketplace';
import { queryKeys } from '../queryKeys';
import type {
  MarketplaceSearchResult,
  MarketplacePluginDetailDto,
  MarketplaceInstallResult,
  BulkUpdateCheckResult,
} from '../types/marketplace';

// ── Internal helper ───────────────────────────────────────────────────────────

/**
 * Returns true when an axios error's HTTP status is 503.
 * Axios wraps the response in `error.response.status`.
 */
function isUnconfiguredError(error: unknown): boolean {
  if (
    error !== null &&
    typeof error === 'object' &&
    'response' in error &&
    (error as { response?: { status?: number } }).response?.status === 503
  ) {
    return true;
  }
  return false;
}

// ── useMarketplaceSearch ──────────────────────────────────────────────────────

export function useMarketplaceSearch(params: {
  query?:    string;
  category?: string;
  page:      number;
  pageSize:  number;
}): {
  data:                      MarketplaceSearchResult | undefined;
  isLoading:                 boolean;
  isError:                   boolean;
  isMarketplaceUnconfigured: boolean;
  error:                     unknown;
  refetch:                   () => void;
} {
  const result = useQuery({
    queryKey: queryKeys.marketplace.search(params),
    queryFn:  ({ signal }) =>
      searchMarketplace(params.query, params.category, params.page, params.pageSize, { signal }),
    staleTime: 60_000,
  });

  const isMarketplaceUnconfigured =
    result.isError && isUnconfiguredError(result.error);

  return {
    data:                      result.data,
    isLoading:                 result.isLoading,
    isError:                   result.isError && !isMarketplaceUnconfigured,
    isMarketplaceUnconfigured,
    error:                     result.error,
    refetch:                   result.refetch,
  };
}

// ── useMarketplacePlugin ──────────────────────────────────────────────────────

export function useMarketplacePlugin(id: string | null): {
  data:                      MarketplacePluginDetailDto | undefined;
  isLoading:                 boolean;
  isError:                   boolean;
  isMarketplaceUnconfigured: boolean;
  error:                     unknown;
} {
  const result = useQuery({
    queryKey: queryKeys.marketplace.detail(id ?? ''),
    queryFn:  ({ signal }) => getMarketplacePlugin(id!, { signal }),
    enabled:  id !== null,
    staleTime: 120_000,
  });

  const isMarketplaceUnconfigured =
    result.isError && isUnconfiguredError(result.error);

  return {
    data:                      result.data,
    isLoading:                 result.isLoading,
    isError:                   result.isError && !isMarketplaceUnconfigured,
    isMarketplaceUnconfigured,
    error:                     result.error,
  };
}

// ── useInstallPlugin ──────────────────────────────────────────────────────────

export function useInstallPlugin(): UseMutationResult<
  MarketplaceInstallResult,
  unknown,
  { id: string; version?: string; name: string }
> {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, version }) =>
      installMarketplacePlugin(id, { version }),
    onSuccess: (result, { name }) => {
      if (result.success) {
        toast.success(
          `"${name}" v${result.installedVersion} installed. Restart required.`,
        );
        void qc.invalidateQueries({ queryKey: queryKeys.plugins.all() });
      } else {
        toast.error(result.errorMessage ?? 'Install failed.');
      }
    },
    onError: () => {
      toast.error('Install failed. Check server logs.');
    },
  });
}

// ── useCheckAllUpdates ────────────────────────────────────────────────────────

export function useCheckAllUpdates(): {
  data:                      BulkUpdateCheckResult | undefined;
  isLoading:                 boolean;
  isMarketplaceUnconfigured: boolean;
  refetch:                   () => void;
} {
  const result = useQuery({
    queryKey: queryKeys.marketplace.updates(),
    queryFn:  async () => {
      try {
        return await checkAllUpdates({ updatesOnly: true });
      } catch (err) {
        if (isUnconfiguredError(err)) {
          // Treat 503 as "unconfigured" — return a sentinel value so the
          // query succeeds (no error state) with a flag the component reads.
          return { totalChecked: 0, updatesAvailable: 0, updates: [], _unconfigured: true } as BulkUpdateCheckResult & { _unconfigured?: boolean };
        }
        throw err;
      }
    },
    staleTime:           300_000,
    refetchOnWindowFocus: false,
  });

  const isMarketplaceUnconfigured =
    (result.data as (BulkUpdateCheckResult & { _unconfigured?: boolean }) | undefined)?._unconfigured === true;

  return {
    data:                      isMarketplaceUnconfigured ? undefined : result.data,
    isLoading:                 result.isLoading,
    isMarketplaceUnconfigured,
    refetch:                   result.refetch,
  };
}

// ── useUpdateCount ────────────────────────────────────────────────────────────

/**
 * Lightweight hook for AppLayout nav badge.
 * Returns 0 on any error, including 503. Never throws.
 */
export function useUpdateCount(): number {
  const result = useQuery({
    queryKey: queryKeys.marketplace.updateCount(),
    queryFn:  async () => {
      try {
        const r = await checkAllUpdates({ updatesOnly: true });
        return r.updatesAvailable;
      } catch {
        return 0;
      }
    },
    staleTime:           300_000,
    refetchOnWindowFocus: false,
  });
  return result.data ?? 0;
}

// ── useUpdatePlugin ───────────────────────────────────────────────────────────

export function useUpdatePlugin(): UseMutationResult<
  MarketplaceInstallResult,
  unknown,
  { id: string; version: string; name: string }
> {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, version }) =>
      installMarketplacePlugin(id, { version }),
    onSuccess: (result, { name }) => {
      if (result.success) {
        toast.success(
          `"${name}" updated to v${result.installedVersion}. Restart required.`,
        );
        void qc.invalidateQueries({ queryKey: queryKeys.plugins.all() });
        void qc.invalidateQueries({ queryKey: queryKeys.marketplace.updates() });
        void qc.invalidateQueries({ queryKey: queryKeys.marketplace.updateCount() });
      } else {
        toast.error(result.errorMessage ?? 'Update failed.');
      }
    },
    onError: () => {
      toast.error('Update failed. Check server logs.');
    },
  });
}

// ── useMarketplaceUnconfigured ────────────────────────────────────────────────

export function useMarketplaceUnconfigured(): boolean {
  return useCheckAllUpdates().isMarketplaceUnconfigured;
}
