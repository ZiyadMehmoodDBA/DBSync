# Task 1: Types + API Layer

> Part of the [Phase 2C.3 master plan](./2026-07-23-phase-2C-3-master.md)

**Goal:** Create the TypeScript type file, raw API function file, and extend `queryKeys.ts`. No hooks, no components. Everything later tasks import originates here.

**Files:**
- Create: `src/MSOSync.Frontend/src/shared/types/marketplace.ts`
- Create: `src/MSOSync.Frontend/src/shared/api/marketplace.ts`
- Modify: `src/MSOSync.Frontend/src/shared/queryKeys.ts`

**Interfaces:**
- Produces: All types in `marketplace.ts` — consumed by every later task
- Produces: `searchMarketplace`, `getMarketplacePlugin`, `getMarketplaceVersions`, `installMarketplacePlugin`, `checkPluginUpdate`, `checkAllUpdates` — consumed by Task 2
- Produces: `queryKeys.marketplace.*` — consumed by Task 2

---

- [ ] **Step 1: Create `shared/types/marketplace.ts`**

Create `src/MSOSync.Frontend/src/shared/types/marketplace.ts` with the following content:

```typescript
// ── Search / List ──────────────────────────────────────────────────────────────

export interface MarketplacePluginListItemDto {
  id:             string;
  name:           string;
  author:         string;
  description:    string;
  category:       string;
  tags:           string[];
  latestVersion:  string;
  minHostVersion: string;
  downloadCount:  number;
  rating:         number;      // 0.0–5.0
  ratingCount:    number;
  publishedAt:    string;      // ISO-8601
  updatedAt:      string;      // ISO-8601
  iconUrl:        string | null;
  verified:       boolean;
}

// ── Detail ────────────────────────────────────────────────────────────────────

export interface MarketplaceVersionDto {
  version:        string;
  minHostVersion: string;
  maxHostVersion: string;
  publishedAt:    string;      // ISO-8601
  downloadUrl:    string;
  sha256:         string;
  releaseNotes:   string | null;
  deprecated:     boolean;
}

export interface MarketplacePluginDetailDto {
  id:             string;
  name:           string;
  author:         string;
  description:    string;
  category:       string;
  tags:           string[];
  latestVersion:  string;
  minHostVersion: string;
  downloadCount:  number;
  rating:         number;
  ratingCount:    number;
  publishedAt:    string;
  updatedAt:      string;
  iconUrl:        string | null;
  projectUrl:     string | null;
  licenseId:      string | null;
  verified:       boolean;
  versions:       MarketplaceVersionDto[];
}

// ── Paged search response envelope ───────────────────────────────────────────

export interface MarketplaceSearchResult {
  data:       MarketplacePluginListItemDto[];
  total:      number;
  page:       number;
  pageSize:   number;
  totalPages: number;
}

// ── Install ───────────────────────────────────────────────────────────────────

export interface MarketplaceInstallRequest {
  version?: string;   // omit to install latest
}

export interface MarketplaceInstallResult {
  success:          boolean;
  pluginId:         string;
  installedVersion: string;
  restartRequired:  boolean;
  errorMessage:     string | null;
}

// ── Update check ──────────────────────────────────────────────────────────────

export interface MarketplaceUpdateManifestDto {
  pluginId:         string;
  installedVersion: string;
  availableVersion: string;
  downloadUrl:      string;
  sha256:           string;
  releaseNotes:     string | null;
  publishedAt:      string;   // ISO-8601
}

export interface BulkUpdateCheckRequest {
  updatesOnly: boolean;
}

export interface BulkUpdateCheckResult {
  totalChecked:     number;
  updatesAvailable: number;
  updates:          MarketplaceUpdateManifestDto[];
}

// ── Search parameters (local, not sent directly — used to build query params) ─

export interface MarketplaceSearchParams {
  query?:    string;
  category?: string;
  page:      number;
  pageSize:  number;
  sort?:     MarketplaceSortOrder;
}

export type MarketplaceSortOrder = 'newest' | 'popular' | 'rating';

// ── Categories (static list, not fetched from backend) ────────────────────────

export const MARKETPLACE_CATEGORIES = [
  'All',
  'Collector',
  'Transformer',
  'Publisher',
  'Routing',
  'Security',
  'Analytics',
  'Utility',
] as const;

export type MarketplaceCategory = typeof MARKETPLACE_CATEGORIES[number];
```

- [ ] **Step 2: Create `shared/api/marketplace.ts`**

Create `src/MSOSync.Frontend/src/shared/api/marketplace.ts` with the following content:

```typescript
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
```

- [ ] **Step 3: Extend `shared/queryKeys.ts` with marketplace keys**

Open `src/MSOSync.Frontend/src/shared/queryKeys.ts`. After the closing brace of the `plugins` group (after `summary: () => ['plugins', 'summary'] as const,`) and before the closing `};` of the `queryKeys` object, add the `marketplace` group:

The file currently ends with:
```typescript
  plugins: {
    all:     () => ['plugins'] as const,
    detail:  (id: string) => ['plugins', id] as const,
    summary: () => ['plugins', 'summary'] as const,
  },
};
```

Change it to:
```typescript
  plugins: {
    all:     () => ['plugins'] as const,
    detail:  (id: string) => ['plugins', id] as const,
    summary: () => ['plugins', 'summary'] as const,
  },

  marketplace: {
    search: (params: { query?: string; category?: string; page: number; pageSize: number }) =>
      ['marketplace', 'search', params] as const,
    detail: (id: string) =>
      ['marketplace', 'plugin', id] as const,
    versions: (id: string) =>
      ['marketplace', 'versions', id] as const,
    updates: () =>
      ['marketplace', 'updates'] as const,
    updateCount: () =>
      ['marketplace', 'update-count'] as const,
  },
};
```

- [ ] **Step 4: Verify TypeScript compiles**

Run from `src/MSOSync.Frontend/`:

```bash
npx tsc --noEmit
```

Expected: no errors related to `marketplace.ts` or `queryKeys.ts`. Fix any import errors before proceeding.

- [ ] **Step 5: Commit**

```bash
git add src/MSOSync.Frontend/src/shared/types/marketplace.ts \
        src/MSOSync.Frontend/src/shared/api/marketplace.ts \
        src/MSOSync.Frontend/src/shared/queryKeys.ts
git commit -m "feat(2C.3-T1): add marketplace types, API functions, and queryKeys"
```
