import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MarketplacePage } from './MarketplacePage';
import type { MarketplaceSearchResult } from '../../shared/types/marketplace';

// ── Mock hooks ────────────────────────────────────────────────────────────────

const mockUseMarketplaceSearch = vi.fn();
const mockInstallMutate        = vi.fn();

vi.mock('../../shared/hooks/useMarketplace', () => ({
  useMarketplaceSearch: (...args: unknown[]) => mockUseMarketplaceSearch(...args),
  useInstallPlugin: () => ({
    mutate:     mockInstallMutate,
    isPending:  false,
    variables:  undefined,
  }),
  useUpdateCount: () => 0,
}));

vi.mock('./hooks', () => ({
  usePlugins: () => ({ data: [], isLoading: false }),
}));

// Drawer uses useMarketplacePlugin — stub to avoid a separate hook mock
vi.mock('./MarketplacePluginDrawer', () => ({
  MarketplacePluginDrawer: ({ pluginId }: { pluginId: string | null }) =>
    pluginId ? <div data-testid="drawer" data-plugin-id={pluginId} /> : null,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeSearchResult(overrides: Partial<MarketplaceSearchResult> = {}): MarketplaceSearchResult {
  return {
    data:       [],
    total:      0,
    page:       1,
    pageSize:   20,
    totalPages: 1,
    ...overrides,
  };
}

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('MarketplacePage', () => {
  beforeEach(() => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult(),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
  });

  it('renders search bar and category filter', () => {
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByRole('textbox', { name: /search plugins/i })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /filter by category/i })).toBeInTheDocument();
  });

  it('renders unconfigured empty state on 503', () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      undefined,
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: true,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByText(/marketplace not configured/i)).toBeInTheDocument();
  });

  it('renders plugin grid when data available', () => {
    const plugins = [
      { id: 'p1', name: 'Plugin Alpha', author: 'A', description: 'desc', category: 'Collector', tags: [], latestVersion: '1.0.0', minHostVersion: '9.0', downloadCount: 100, rating: 4.0, ratingCount: 10, publishedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z', iconUrl: null, verified: false },
      { id: 'p2', name: 'Plugin Beta',  author: 'B', description: 'desc', category: 'Utility',   tags: [], latestVersion: '2.0.0', minHostVersion: '9.0', downloadCount: 200, rating: 4.5, ratingCount: 20, publishedAt: '2026-02-01T00:00:00Z', updatedAt: '2026-07-01T00:00:00Z', iconUrl: null, verified: false },
      { id: 'p3', name: 'Plugin Gamma', author: 'C', description: 'desc', category: 'Security',  tags: [], latestVersion: '3.0.0', minHostVersion: '9.0', downloadCount: 300, rating: 5.0, ratingCount: 30, publishedAt: '2026-03-01T00:00:00Z', updatedAt: '2026-07-15T00:00:00Z', iconUrl: null, verified: true },
    ];
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ data: plugins, total: 3 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByText('Plugin Alpha')).toBeInTheDocument();
    expect(screen.getByText('Plugin Beta')).toBeInTheDocument();
    expect(screen.getByText('Plugin Gamma')).toBeInTheDocument();
  });

  it('renders loading skeleton while fetching', () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      undefined,
      isLoading:                 true,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    // 12 skeleton cards with animate-pulse
    const skeletons = document.querySelectorAll('.animate-pulse');
    expect(skeletons.length).toBe(12);
  });

  it('renders error state on non-503 error', () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      undefined,
      isLoading:                 false,
      isError:                   true,
      isMarketplaceUnconfigured: false,
      error:                     new Error('Network error'),
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByText(/network error/i)).toBeInTheDocument();
  });

  it('calls useInstallPlugin.mutate when Install button clicked', async () => {
    const plugin = { id: 'p1', name: 'Plugin Alpha', author: 'A', description: 'desc', category: 'Collector', tags: [], latestVersion: '1.0.0', minHostVersion: '9.0', downloadCount: 100, rating: 4.0, ratingCount: 10, publishedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z', iconUrl: null, verified: false };
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ data: [plugin], total: 1 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    await userEvent.click(screen.getByRole('button', { name: /install plugin alpha/i }));
    expect(mockInstallMutate).toHaveBeenCalledWith({ id: 'p1', name: 'Plugin Alpha' });
  });

  it('opens drawer when plugin card body clicked', async () => {
    const plugin = { id: 'p1', name: 'Plugin Alpha', author: 'A', description: 'desc alpha', category: 'Collector', tags: [], latestVersion: '1.0.0', minHostVersion: '9.0', downloadCount: 100, rating: 4.0, ratingCount: 10, publishedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z', iconUrl: null, verified: false };
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ data: [plugin], total: 1 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    await userEvent.click(screen.getByText('desc alpha'));
    expect(screen.getByTestId('drawer')).toHaveAttribute('data-plugin-id', 'p1');
  });

  it('pagination Next button increments page', async () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ total: 40, page: 1, totalPages: 2 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    const nextBtn = screen.getByRole('button', { name: /next/i });
    await userEvent.click(nextBtn);
    await waitFor(() => {
      // After clicking Next, page state becomes 2.
      // The hook is called again — verify the call args include page 2.
      const calls = mockUseMarketplaceSearch.mock.calls;
      const lastCall = calls[calls.length - 1][0];
      expect(lastCall.page).toBe(2);
    });
  });
});
