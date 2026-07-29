import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { SloStatusCard } from '../SloStatusCard';

describe('SloStatusCard', () => {
  it('shows SLO Met when met=true', () => {
    render(<SloStatusCard label="Delivery Rate" value="99.95%" target="≥ 99.9%" met={true} />);
    expect(screen.getByText('✓ SLO Met')).toBeInTheDocument();
  });

  it('shows SLO Breached when met=false', () => {
    render(<SloStatusCard label="P99 Latency" value="6000ms" target="≤ 5000ms" met={false} />);
    expect(screen.getByText('✗ SLO Breached')).toBeInTheDocument();
  });
});
