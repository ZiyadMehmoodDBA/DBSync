import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { NodeHealthTable } from '../NodeHealthTable';
import type { NodeHealthScore } from '../../../../shared/types/observability';

const mockScores: NodeHealthScore[] = [
  {
    nodeId: 'node-1',
    nodeName: 'Node Alpha',
    score: 95,
    grade: 'A',
    connectivityScore: 40,
    syncLagScore: 30,
    errorRateScore: 20,
    heartbeatScore: 5,
    computedAt: new Date().toISOString(),
  },
];

describe('NodeHealthTable', () => {
  it('renders node name and grade', () => {
    render(<NodeHealthTable scores={mockScores} loading={false} />);
    expect(screen.getByText('Node Alpha')).toBeInTheDocument();
    expect(screen.getByText('A')).toBeInTheDocument();
  });

  it('shows loading message when loading=true', () => {
    render(<NodeHealthTable scores={[]} loading={true} />);
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('shows empty message when no nodes', () => {
    render(<NodeHealthTable scores={[]} loading={false} />);
    expect(screen.getByText(/no sync nodes/i)).toBeInTheDocument();
  });
});
