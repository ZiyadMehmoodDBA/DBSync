// features/node-management/nodes/components/__tests__/LifecycleHistoryTimeline.test.tsx
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { TimelineEntry } from '../LifecycleHistoryTimeline';

describe('TimelineEntry', () => {
  const entry = {
    historyId: 1, nodeId: 'n1', fromState: 'PendingRegistration' as const,
    toState: 'Active' as const, trigger: 'Activation' as const, reason: null,
    actor: 'system', correlationId: 'abc-123', metadataJson: null,
    occurredAt: '2026-07-06T10:00:00Z',
  };

  it('shows from → to, trigger, actor', () => {
    render(<TimelineEntry entry={entry} />);
    expect(screen.getByText(/PendingRegistration/)).toBeInTheDocument();
    expect(screen.getByText(/Active/)).toBeInTheDocument();
    expect(screen.getByText(/Activation/)).toBeInTheDocument();
    expect(screen.getByText(/system/)).toBeInTheDocument();
  });

  it('renders migration seed rows (fromState null) as "entered lifecycle model"', () => {
    render(<TimelineEntry entry={{ ...entry, fromState: null, trigger: 'Migration' }} />);
    expect(screen.getByText(/entered lifecycle model/i)).toBeInTheDocument();
  });

  it('hides CorrelationId behind a collapsible detail', () => {
    render(<TimelineEntry entry={entry} />);
    expect(screen.queryByText('abc-123')).not.toBeVisible();
  });
});
