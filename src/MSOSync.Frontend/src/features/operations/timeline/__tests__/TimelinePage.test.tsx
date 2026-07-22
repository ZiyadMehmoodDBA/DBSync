import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import TimelinePage from '../TimelinePage';
import * as api from '@/shared/api/operationTimeline';
import type { OperationTimelineDto } from '@/shared/types/timeline';

vi.mock('@/shared/api/operationTimeline');
// Recharts resize observer not available in JSDOM
vi.mock('recharts', () => ({
  BarChart: ({ children }: { children: React.ReactNode }) => <div data-testid="bar-chart">{children}</div>,
  Bar: () => null,
  XAxis: () => null,
  YAxis: () => null,
  Tooltip: () => null,
  ResponsiveContainer: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  Cell: () => null,
}));

const emptyTimeline: OperationTimelineDto = {
  items: [], from: '', to: '', hasMore: false, returnedCount: 0,
};

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('TimelinePage', () => {
  beforeEach(() => {
    vi.mocked(api.getOperationTimeline).mockResolvedValue(emptyTimeline);
  });

  it('renders page heading', async () => {
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText('Operations Timeline')).toBeInTheDocument();
  });

  it('shows empty state message when no operations', async () => {
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText('No operations in this range.')).toBeInTheDocument();
  });

  it('renders chart when operations present', async () => {
    const now = Date.now();
    const data: OperationTimelineDto = {
      items: [{
        operationId: 'op-1', type: 'Export', status: 'Completed',
        label: 'Export job', startedAt: new Date(now - 60_000).toISOString(),
        completedAt: new Date(now - 30_000).toISOString(), progressPercent: 100,
      }],
      from: new Date(now - 3_600_000).toISOString(),
      to:   new Date(now).toISOString(),
      hasMore: false, returnedCount: 1,
    };
    vi.mocked(api.getOperationTimeline).mockResolvedValue(data);
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByTestId('bar-chart')).toBeInTheDocument();
  });

  it('shows HasMore banner when hasMore is true', async () => {
    vi.mocked(api.getOperationTimeline).mockResolvedValue({
      ...emptyTimeline,
      hasMore: true, returnedCount: 200,
      items: [],
    });
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText(/narrow the time range/i)).toBeInTheDocument();
  });

  it('renders type filter buttons', async () => {
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText('Export')).toBeInTheDocument();
    expect(await screen.findByText('BatchReplay')).toBeInTheDocument();
  });
});
