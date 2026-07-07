import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LifecycleBadge } from '../LifecycleBadge';
import { ConnectivityBadge } from '../ConnectivityBadge';
import { MaintenanceBadge } from '../MaintenanceBadge';
import { NodeStatusSummary } from '../NodeStatusSummary';

describe('LifecycleBadge', () => {
  it('renders label text (never color-only)', () => {
    render(<LifecycleBadge state="Active" />);
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('renders an icon for every state', () => {
    const states = [
      'PendingApproval', 'PendingRegistration', 'Active', 'Recovery',
      'Disabled', 'Decommissioning', 'Decommissioned', 'Rejected',
    ] as const;
    for (const s of states) {
      const { container, unmount } = render(<LifecycleBadge state={s} />);
      expect(container.querySelector('svg')).not.toBeNull();
      unmount();
    }
  });
});

describe('ConnectivityBadge', () => {
  it('shows reason as title tooltip when provided', () => {
    render(<ConnectivityBadge status="Degraded" reason="HeartbeatStale" />);
    expect(screen.getByText('Degraded')).toBeInTheDocument();
    expect(screen.getByTitle('HeartbeatStale')).toBeInTheDocument();
  });
});

describe('MaintenanceBadge', () => {
  it('renders nothing when not in maintenance', () => {
    const { container } = render(<MaintenanceBadge active={false} />);
    expect(container.firstChild).toBeNull();
  });
  it('renders Maintenance label when active', () => {
    render(<MaintenanceBadge active reason="patching" />);
    expect(screen.getByText('Maintenance')).toBeInTheDocument();
  });
});

describe('NodeStatusSummary', () => {
  it('composes all three dimensions', () => {
    render(
      <NodeStatusSummary
        lifecycle="Active"
        connectivity="Reachable"
        connectivityReason="Healthy"
        maintenance
        maintenanceReason="patch window"
      />,
    );
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Reachable')).toBeInTheDocument();
    expect(screen.getByText('Maintenance')).toBeInTheDocument();
  });
});
