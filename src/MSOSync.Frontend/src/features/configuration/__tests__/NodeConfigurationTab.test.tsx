import { render, screen } from '@testing-library/react';
import { NodeConfigurationTab } from '../NodeConfigurationTab';
import { describe, it, expect, vi } from 'vitest';

vi.mock('../hooks', () => ({
  useNodeConfiguration: () => ({ data: null, isLoading: false, error: null }),
  useNodeConfigurationHistory: () => ({ data: [] }),
}));

describe('NodeConfigurationTab', () => {
  it('shows None state when no config', () => {
    render(<NodeConfigurationTab nodeId="node-1" />);
    expect(screen.getByText('State:')).toBeInTheDocument();
    expect(screen.getByText('None')).toBeInTheDocument();
  });

  it('shows no template message', () => {
    render(<NodeConfigurationTab nodeId="node-1" />);
    expect(screen.getByText(/no template assigned/i)).toBeInTheDocument();
  });
});
