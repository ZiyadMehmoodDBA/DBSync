import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfigComparePanel } from '../ConfigComparePanel';
import * as api from '@/shared/api/configComparison';
import type { ConfigVersionDiffDto } from '@/shared/types/configComparison';

vi.mock('@/shared/api/configComparison');

const versions = [
  { versionNumber: 1, label: 'v1 (Published 2026-07-01)' },
  { versionNumber: 2, label: 'v2 (Draft)' },
];

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('ConfigComparePanel', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows prompt when no versions selected', () => {
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    expect(screen.getByText(/select two different/i)).toBeInTheDocument();
  });

  it('shows "no differences" for identical result', async () => {
    const diff: ConfigVersionDiffDto = {
      templateId: 't1', v1: 1, v2: 2, v1Label: 'v1', v2Label: 'v2',
      hasChanges: false,
      entries: [{ key: 'host', changeType: 'Unchanged', oldValue: 's1', newValue: 's1' }],
    };
    vi.mocked(api.getConfigVersionDiff).mockResolvedValue(diff);
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: '1' } });
    fireEvent.change(screen.getAllByRole('combobox')[1], { target: { value: '2' } });
    expect(await screen.findByText('No differences')).toBeInTheDocument();
  });

  it('renders diff table rows', async () => {
    const diff: ConfigVersionDiffDto = {
      templateId: 't1', v1: 1, v2: 2, v1Label: 'v1', v2Label: 'v2',
      hasChanges: true,
      entries: [
        { key: 'database.host', changeType: 'Changed', oldValue: 's1', newValue: 's2' },
      ],
    };
    vi.mocked(api.getConfigVersionDiff).mockResolvedValue(diff);
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: '1' } });
    fireEvent.change(screen.getAllByRole('combobox')[1], { target: { value: '2' } });
    expect(await screen.findByText('database.host')).toBeInTheDocument();
    expect(await screen.findByText('s1')).toBeInTheDocument();
  });

  it('shows unchanged toggle when unchanged entries exist', async () => {
    const diff: ConfigVersionDiffDto = {
      templateId: 't1', v1: 1, v2: 2, v1Label: 'v1', v2Label: 'v2',
      hasChanges: true,
      entries: [
        { key: 'host', changeType: 'Changed', oldValue: 's1', newValue: 's2' },
        { key: 'port', changeType: 'Unchanged', oldValue: '5432', newValue: '5432' },
      ],
    };
    vi.mocked(api.getConfigVersionDiff).mockResolvedValue(diff);
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={vi.fn()} />,
      { wrapper },
    );
    fireEvent.change(screen.getAllByRole('combobox')[0], { target: { value: '1' } });
    fireEvent.change(screen.getAllByRole('combobox')[1], { target: { value: '2' } });
    expect(await screen.findByText(/show 1 unchanged/i)).toBeInTheDocument();
  });

  it('calls onClose when X button clicked', () => {
    const onClose = vi.fn();
    render(
      <ConfigComparePanel templateId="t1" availableVersions={versions} onClose={onClose} />,
      { wrapper },
    );
    fireEvent.click(screen.getByRole('button', { name: /close/i }));
    expect(onClose).toHaveBeenCalledOnce();
  });
});
