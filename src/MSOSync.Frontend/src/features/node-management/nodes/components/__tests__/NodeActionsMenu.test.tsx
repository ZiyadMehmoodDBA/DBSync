// features/node-management/nodes/components/__tests__/NodeActionsMenu.test.tsx
// Pattern: render with QueryClientProvider wrapper; mock shared/api/lifecycle with vi.mock.
import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { NodeActionsMenu } from '../NodeActionsMenu';

vi.mock('../../../../../shared/api/lifecycle', () => ({
  getNodeTransitions: vi.fn().mockResolvedValue({
    currentState: 'Active',
    allowedTransitions: [
      { action: 'Disable',          requiresReason: false, requiresConfirmation: true,  dangerLevel: 'Normal' },
      { action: 'StartMaintenance', requiresReason: true,  requiresConfirmation: false, dangerLevel: 'Normal' },
      { action: 'Decommission',     requiresReason: true,  requiresConfirmation: true,  dangerLevel: 'Critical' },
    ],
  }),
  enableNode: vi.fn(), disableNode: vi.fn(), startMaintenance: vi.fn(), endMaintenance: vi.fn(),
  decommissionNode: vi.fn(), forceCompleteDecommission: vi.fn(),
  getNodeState: vi.fn(), getNodeLifecycleHistory: vi.fn(),
}));

function wrap(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe('NodeActionsMenu', () => {
  it('renders exactly the actions the backend returns — no hardcoded rules', async () => {
    wrap(<NodeActionsMenu nodeId="n1" canManage onAction={() => {}} />);
    await userEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(screen.getByText('Disable')).toBeInTheDocument());
    expect(screen.getByText('Start Maintenance')).toBeInTheDocument();
    expect(screen.getByText('Decommission')).toBeInTheDocument();
    expect(screen.queryByText('Enable')).not.toBeInTheDocument();
  });

  it('hides mutating actions without MANAGE_NODE_LIFECYCLE', async () => {
    wrap(<NodeActionsMenu nodeId="n1" canManage={false} onAction={() => {}} />);
    await userEvent.click(screen.getByRole('button'));
    await waitFor(() =>
      expect(screen.getByText(/no permitted actions|view only/i)).toBeInTheDocument());
  });
});
