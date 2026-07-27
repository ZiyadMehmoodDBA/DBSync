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
