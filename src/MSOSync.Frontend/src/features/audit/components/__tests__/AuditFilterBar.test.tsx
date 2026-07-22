import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AuditFilterBar, type AuditFilterState } from '../AuditFilterBar';

const empty: AuditFilterState = {
  usernames: [], actionNames: [], objectNames: [], from: '', to: '',
};

describe('AuditFilterBar', () => {
  it('renders all filter sections', () => {
    render(
      <AuditFilterBar value={empty} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.getByText('Usernames')).toBeInTheDocument();
    expect(screen.getByText('Actions')).toBeInTheDocument();
    expect(screen.getByText('Object Names')).toBeInTheDocument();
  });

  it('shows Clear All when filter is active', () => {
    const active: AuditFilterState = { ...empty, usernames: ['alice'] };
    render(
      <AuditFilterBar value={active} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.getByRole('button', { name: /clear all/i })).toBeInTheDocument();
  });

  it('calls onChange with empty filter on Clear All click', () => {
    const onChange = vi.fn();
    const active: AuditFilterState = { ...empty, usernames: ['alice'] };
    render(
      <AuditFilterBar value={active} onChange={onChange} onSave={vi.fn()} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /clear all/i }));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ usernames: [] }));
  });

  it('does not show Clear All when filter is empty', () => {
    render(
      <AuditFilterBar value={empty} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.queryByRole('button', { name: /clear all/i })).not.toBeInTheDocument();
  });

  it('shows chip for existing username value', () => {
    const active: AuditFilterState = { ...empty, usernames: ['alice'] };
    render(
      <AuditFilterBar value={active} onChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.getByText('alice')).toBeInTheDocument();
  });
});
