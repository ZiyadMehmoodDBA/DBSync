import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ClusterPage from '../ClusterPage';
import * as clusterApi from '@/shared/api/cluster';
import type { ClusterSummaryDto } from '@/shared/types/cluster';

vi.mock('@/shared/api/cluster');

const emptySummary: ClusterSummaryDto = {
  nodeStates: { total: 0, active: 0, maintenance: 0, draining: 0, offline: 0 },
  operationCounts: { running: 0, pending: 0, succeededToday: 0, failedToday: 0 },
  activeOperations: [],
  activeRollingOps: [],
  activeReplays: [],
  recentNodeChanges: [],
};

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('ClusterPage', () => {
  beforeEach(() => {
    vi.mocked(clusterApi.getClusterSummary).mockResolvedValue(emptySummary);
  });

  it('shows zero counts on empty data', async () => {
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('0 total nodes')).toBeInTheDocument();
  });

  it('shows no active operations message', async () => {
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('No active operations')).toBeInTheDocument();
  });

  it('shows no rolling operations message', async () => {
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('No active rolling operations')).toBeInTheDocument();
  });

  it('renders active operation when present', async () => {
    const summary = {
      ...emptySummary,
      operationCounts: { running: 1, pending: 0, succeededToday: 0, failedToday: 0 },
      activeOperations: [{
        operationId: 'op-1', type: 'BatchReplay', status: 'Running',
        nodeId: null, progressPercent: 42, progressMessage: 'Processing…',
        startedAt: new Date().toISOString(),
      }],
    };
    vi.mocked(clusterApi.getClusterSummary).mockResolvedValue(summary);
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('BatchReplay')).toBeInTheDocument();
  });

  it('shows recent node change strip', async () => {
    const summary = {
      ...emptySummary,
      recentNodeChanges: [{
        nodeId: 'node-abc', fromState: 'Active', toState: 'Maintenance',
        trigger: 'ManualAction', occurredAt: new Date().toISOString(),
      }],
    };
    vi.mocked(clusterApi.getClusterSummary).mockResolvedValue(summary);
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('node-abc')).toBeInTheDocument();
  });
});
