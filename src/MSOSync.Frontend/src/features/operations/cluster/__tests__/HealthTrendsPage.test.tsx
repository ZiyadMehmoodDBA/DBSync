import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import HealthTrendsPage from '../HealthTrendsPage';

vi.mock('@/shared/hooks/useHealthTrends', () => ({
  useHealthTrends: vi.fn(),
}));

import { useHealthTrends } from '@/shared/hooks/useHealthTrends';

const mockData = {
  window: '6h',
  bucketCount: 12,
  buckets: Array.from({ length: 12 }, (_, i) => ({
    bucketStart: new Date(Date.now() - (12 - i) * 30 * 60 * 1000).toISOString(),
    reachableCount: 3,
    degradedCount: 0,
    unreachableCount: 0,
    totalNodes: 3,
    transitionCount: 0,
  })),
  nodeProbeStats: [
    { nodeId: 'node-1', connectivityStatus: 'Reachable', lastProbeLatencyMs: null, consecutiveProbeFailures: 0, uptimePct: 100 },
    { nodeId: 'node-2', connectivityStatus: 'Degraded',  lastProbeLatencyMs: null, consecutiveProbeFailures: 1, uptimePct: 80  },
  ],
};

describe('HealthTrendsPage', () => {
  it('renders loading state', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: true, error: null });
    render(<HealthTrendsPage />);
    expect(screen.getByText(/loading health trends/i)).toBeTruthy();
  });

  it('renders chart and node stats table when data loaded', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: mockData, isLoading: false, error: null });
    render(<HealthTrendsPage />);
    expect(screen.getByText('node-1')).toBeTruthy();
    expect(screen.getByText('node-2')).toBeTruthy();
    expect(screen.getByText('Reachable')).toBeTruthy();
    expect(screen.getByText('Degraded')).toBeTruthy();
  });

  it('window selector buttons are rendered', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: mockData, isLoading: false, error: null });
    render(<HealthTrendsPage />);
    expect(screen.getByText('1h')).toBeTruthy();
    expect(screen.getByText('24h')).toBeTruthy();
    expect(screen.getByText('7d')).toBeTruthy();
  });

  it('renders empty state when no node probe stats', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({
      data: { ...mockData, nodeProbeStats: [] },
      isLoading: false,
      error: null,
    });
    render(<HealthTrendsPage />);
    expect(screen.getByText(/no connectivity data/i)).toBeTruthy();
  });

  it('renders error state', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') });
    render(<HealthTrendsPage />);
    expect(screen.getByText(/failed to load health trends/i)).toBeTruthy();
  });
});
