import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import RecoveryDashboardPage from '../RecoveryDashboardPage';

vi.mock('@/shared/hooks/useRecoveryDashboard', () => ({
  useRecoveryDashboard: vi.fn(),
}));

import { useRecoveryDashboard } from '@/shared/hooks/useRecoveryDashboard';

const emptyData = {
  summary: { activeCount: 0, avgRtoMinutes: null, maxRtoMinutes: null, completedLast30Days: 0 },
  activeRecoveries: [],
  recentCompletedRecoveries: [],
};

describe('RecoveryDashboardPage', () => {
  it('renders summary cards', () => {
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data: emptyData, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText('Active Recoveries')).toBeTruthy();
    expect(screen.getByText('Avg RTO (min)')).toBeTruthy();
    expect(screen.getByText('Completed (30d)')).toBeTruthy();
  });

  it('shows empty state when no active recoveries', () => {
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data: emptyData, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText(/no nodes currently in recovery/i)).toBeTruthy();
  });

  it('renders active recovery row with elapsed time', () => {
    const data = {
      ...emptyData,
      activeRecoveries: [{
        nodeId: 'node-x', failureDetectedAt: null,
        recoveryStartedAt: new Date(Date.now() - 30 * 60 * 1000).toISOString(),
        elapsedMinutes: 30.0, associatedReplayOps: [],
      }],
    };
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText('node-x')).toBeTruthy();
    expect(screen.getByText('30.0')).toBeTruthy();
  });

  it('renders replay op status chip for active recovery', () => {
    const data = {
      ...emptyData,
      activeRecoveries: [{
        nodeId: 'node-y', failureDetectedAt: null,
        recoveryStartedAt: new Date().toISOString(),
        elapsedMinutes: 5.0,
        associatedReplayOps: [{ operationId: 'op-1', status: 'Running', itemsDone: 1, itemsTotal: 5 }],
      }],
    };
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText('Running')).toBeTruthy();
  });

  it('renders error state', () => {
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText(/failed to load recovery dashboard/i)).toBeTruthy();
  });
});
