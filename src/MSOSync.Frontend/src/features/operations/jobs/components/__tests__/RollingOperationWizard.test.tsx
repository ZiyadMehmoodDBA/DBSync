import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RollingOperationWizard } from '../RollingOperationWizard';
import type { CreateRollingOperationRequest } from '@/shared/types/rolling';

const mockMutate = vi.fn();

vi.mock('@/shared/api/nodes', () => ({
  getAllNodes: vi.fn().mockResolvedValue([
    { nodeId: 'n1', lifecycleState: 'Active', groupId: 'g1', syncUrl: '', heartbeatInterval: 60,
      canSynchronize: true, transportMode: 'Pull', connectivityStatus: 'Reachable',
      maintenanceMode: false, hasDbPassword: false },
    { nodeId: 'n2', lifecycleState: 'Active', groupId: 'g1', syncUrl: '', heartbeatInterval: 60,
      canSynchronize: true, transportMode: 'Pull', connectivityStatus: 'Reachable',
      maintenanceMode: false, hasDbPassword: false },
  ]),
}));

vi.mock('@/shared/hooks/useRollingOperations', () => ({
  useCreateRollingOperation: () => ({
    mutate: mockMutate,
    isPending: false,
  }),
}));

// Stub Radix Select — jsdom lacks hasPointerCapture; use a native <select> instead.
vi.mock('@/components/ui/select', () => ({
  Select: ({ value, onValueChange, children }: {
    value?: string; onValueChange?: (v: string) => void; children?: React.ReactNode
  }) => (
    <select value={value} onChange={(e) => onValueChange?.(e.target.value)} data-testid="select-stub">
      {children}
    </select>
  ),
  SelectTrigger: ({ children }: { children?: React.ReactNode }) => <>{children}</>,
  SelectValue:   ({ placeholder }: { placeholder?: string }) => <option value="">{placeholder}</option>,
  SelectContent: ({ children }: { children?: React.ReactNode }) => <>{children}</>,
  SelectItem:    ({ value, children }: { value: string; children?: React.ReactNode }) => (
    <option value={value}>{children}</option>
  ),
}));

function wrap(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe('RollingOperationWizard', () => {
  it('disables submit until at least one node selected', () => {
    wrap(<RollingOperationWizard open onOpenChange={() => {}} />);
    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();
  });

  it('requires target version for RollingUpgrade', async () => {
    const user = userEvent.setup();
    wrap(<RollingOperationWizard open onOpenChange={() => {}} />);

    const selects = screen.getAllByTestId('select-stub');
    await user.selectOptions(selects[0]!, 'RollingUpgrade');

    expect(screen.getByText(/TargetVersion is required for RollingUpgrade/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled();
  });

  it('shows window seconds field when auto-window selected', async () => {
    const user = userEvent.setup();
    wrap(<RollingOperationWizard open onOpenChange={() => {}} />);

    const selects = screen.getAllByTestId('select-stub');
    await user.selectOptions(selects[1]!, 'auto-window');

    expect(screen.getByDisplayValue('600')).toBeInTheDocument();
  });

  it('submits camelCase payload with selected nodes', async () => {
    const user = userEvent.setup();
    wrap(<RollingOperationWizard open onOpenChange={() => {}} />);

    const n1checkbox = await screen.findByRole('checkbox', { name: /n1/ });
    await user.click(n1checkbox);

    const createBtn = screen.getByRole('button', { name: 'Create' });
    expect(createBtn).not.toBeDisabled();
    await user.click(createBtn);

    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining<Partial<CreateRollingOperationRequest>>({
        kind: 'RollingMaintenance',
        nodeIds: ['n1'],
        gateSoakSeconds: 60,
        waveAction: 'manual-confirm',
        verificationTimeoutSeconds: 900,
      }),
      expect.anything(),
    );
  });
});
