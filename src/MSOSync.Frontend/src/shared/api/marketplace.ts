import client from './client';
import type {
  MarketplaceSearchResult,
  MarketplacePluginDetailDto,
  MarketplaceVersionDto,
  MarketplaceInstallRequest,
  MarketplaceInstallResult,
  MarketplaceUpdateManifestDto,
  BulkUpdateCheckRequest,
  BulkUpdateCheckResult,
} from '../types/marketplace';

// ── Search ────────────────────────────────────────────────────────────────────

export async function searchMarketplace(
  query:    string | undefined,
  category: string | undefined,
  page:     number,
  pageSize: number,
  options?: { signal?: AbortSignal },
): Promise<MarketplaceSearchResult> {
  const { data } = await client.get<MarketplaceSearchResult>(
    '/marketplace/plugins',
    {
      params: {
        ...(query                                ? { query }    : {}),
        ...(category && category !== 'All'       ? { category } : {}),
        page,
        pageSize,
      },
      signal: options?.signal,
    },
  );
  return data;
}

// ── Detail ────────────────────────────────────────────────────────────────────

export async function getMarketplacePlugin(
  id:      string,
  options?: { signal?: AbortSignal },
): Promise<MarketplacePluginDetailDto> {
  const { data } = await client.get<MarketplacePluginDetailDto>(
    `/marketplace/plugins/${encodeURIComponent(id)}`,
    { signal: options?.signal },
  );
  return data;
}

// ── Versions ──────────────────────────────────────────────────────────────────

export async function getMarketplaceVersions(
  id:      string,
  options?: { signal?: AbortSignal },
): Promise<MarketplaceVersionDto[]> {
  const { data } = await client.get<MarketplaceVersionDto[]>(
    `/marketplace/plugins/${encodeURIComponent(id)}/versions`,
    { signal: options?.signal },
  );
  return data;
}

// ── Install ───────────────────────────────────────────────────────────────────

export async function installMarketplacePlugin(
  id:      string,
  request: MarketplaceInstallRequest,
): Promise<MarketplaceInstallResult> {
  const { data } = await client.post<MarketplaceInstallResult>(
    `/marketplace/plugins/${encodeURIComponent(id)}/install`,
    request,
  );
  return data;
}

// ── Single plugin update check ────────────────────────────────────────────────

export async function checkPluginUpdate(
  id:      string,
  options?: { signal?: AbortSignal },
): Promise<MarketplaceUpdateManifestDto | null> {
  // Backend returns 204 when no update is available.
  // Axios resolves 204 with data = '' — normalise to null.
  const { data, status } = await client.get<MarketplaceUpdateManifestDto | ''>(
    `/marketplace/plugins/${encodeURIComponent(id)}/updates`,
    { signal: options?.signal },
  );
  if (status === 204 || data === '') return null;
  return data as MarketplaceUpdateManifestDto;
}

// ── Bulk update check ─────────────────────────────────────────────────────────

export async function checkAllUpdates(
  request: BulkUpdateCheckRequest = { updatesOnly: true },
): Promise<BulkUpdateCheckResult> {
  const { data } = await client.post<BulkUpdateCheckResult>(
    '/marketplace/updates/check',
    request,
  );
  return data;
}
