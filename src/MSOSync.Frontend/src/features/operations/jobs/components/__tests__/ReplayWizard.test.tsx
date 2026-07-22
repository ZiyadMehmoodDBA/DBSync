import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReplayWizard } from '../ReplayWizard';

const mockMutate = vi.fn();

vi.mock('@/shared/hooks/useReplayOperations', () => ({
  useCreateReplay: () => ({
    mutate:    mockMutate,
    isPending: false,
  }),
}));

function wrap(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe('ReplayWizard', () => {
  it('renders step 1 mode selection', () => {
    wrap(<ReplayWizard open onOpenChange={() => {}} />);
    expect(screen.getByText(/Replay Mode/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Failed Delivery/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Missed Data/i)).toBeInTheDocument();
  });

  it('advances to step 2 on Next click', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByLabelText(/Target Node/i)).toBeInTheDocument();
  });

  it('hides Batch IDs field when mode is MissedData', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    // Select MissedData
    await user.click(screen.getByLabelText(/Missed Data/i));

    // Advance to step 2, 3, 4
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.type(screen.getByLabelText(/Target Node/i), 'n1');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    // Step 3: advance without filling dates (range validation returns false when empty)
    // Use Back/Next workaround: manually advance by removing range check blockage
    // The step 3 "Next" validates range; with empty inputs validateRange returns false.
    // We need to fill dates to advance.
    const fromInput = screen.getByLabelText(/From/i);
    const toInput   = screen.getByLabelText(/To/i);
    await user.type(fromInput, '2026-01-01T00:00');
    await user.type(toInput,   '2026-01-02T00:00');
    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(screen.queryByLabelText(/Batch IDs/i)).not.toBeInTheDocument();
  });

  it('shows Batch IDs field when mode is FailedDelivery in step 4', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    // FailedDelivery is default — advance to step 4
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.type(screen.getByLabelText(/Target Node/i), 'n1');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    const fromInput = screen.getByLabelText(/From/i);
    const toInput   = screen.getByLabelText(/To/i);
    await user.type(fromInput, '2026-01-01T00:00');
    await user.type(toInput,   '2026-01-02T00:00');
    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(screen.getByLabelText(/Batch IDs/i)).toBeInTheDocument();
  });

  it('calls createReplay with correct payload on submit', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.type(screen.getByLabelText(/Target Node/i), 'n1');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    const fromInput = screen.getByLabelText(/From/i);
    const toInput   = screen.getByLabelText(/To/i);
    await user.type(fromInput, '2026-01-01T00:00');
    await user.type(toInput,   '2026-01-02T00:00');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.click(screen.getByRole('button', { name: 'Start Replay' }));

    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({ nodeId: 'n1', replayMode: 'FailedDelivery' }),
      expect.any(Object),
    );
  });
});
