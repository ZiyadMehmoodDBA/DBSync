import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import ClusterDiagnosticsPage from '../ClusterDiagnosticsPage';

vi.mock('@/shared/hooks/useClusterDiagnostics', () => ({
  useClusterDiagnostics: vi.fn(),
}));

import { useClusterDiagnostics } from '@/shared/hooks/useClusterDiagnostics';

const emptyData = { runtimeStats: [], activeLocks: [], slowOperations: [] };

describe('ClusterDiagnosticsPage', () => {
  it('renders empty states for all panels', () => {
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data: emptyData, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText(/no runtime stats available/i)).toBeTruthy();
    expect(screen.getByText(/no active locks/i)).toBeTruthy();
    expect(screen.getByText(/no running or pending operations/i)).toBeTruthy();
  });

  it('renders stale lock row highlighted', () => {
    const data = {
      ...emptyData,
      activeLocks: [{ lockName: 'stale-lock', lockOwner: 'w1', ageSeconds: 400, isStale: true }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText('Stale')).toBeTruthy();
    expect(screen.getByText('stale-lock')).toBeTruthy();
  });

  it('renders fresh lock as Active', () => {
    const data = {
      ...emptyData,
      activeLocks: [{ lockName: 'fresh-lock', lockOwner: 'w2', ageSeconds: 30, isStale: false }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText('Active')).toBeTruthy();
  });

  it('renders slow op progress bar', () => {
    const data = {
      ...emptyData,
      slowOperations: [{ operationId: 'op-1', operationType: 'Export', status: 'Running', durationMinutes: 12.5, progressPercent: 60 }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText('Export')).toBeTruthy();
    expect(screen.getByText('60%')).toBeTruthy();
  });

  it('renders runtime stats summary card with latest entry', () => {
    const data = {
      ...emptyData,
      runtimeStats: [{ statId: 1, heapUsedMb: 256, heapMaxMb: 512, cpuPercent: 33.3, threadCount: 50, gcCount: 100, uptimeHours: 4.5, capturedAt: new Date().toISOString() }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText(/256\.0 MB/)).toBeTruthy();
  });

  it('renders error state', () => {
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText(/failed to load diagnostics/i)).toBeTruthy();
  });
});
