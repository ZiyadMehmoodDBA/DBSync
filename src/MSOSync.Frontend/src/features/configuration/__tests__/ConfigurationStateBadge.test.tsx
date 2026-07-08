import { render, screen } from '@testing-library/react';
import { ConfigurationStateBadge } from '../../../components/ui/ConfigurationStateBadge';
import { describe, it, expect } from 'vitest';

describe('ConfigurationStateBadge', () => {
  it('renders Current state', () => {
    render(<ConfigurationStateBadge state="Current" />);
    expect(screen.getByText('Current')).toBeInTheDocument();
    expect(screen.getByRole('status')).toBeTruthy();
  });

  it('renders UpdateAvailable state', () => {
    render(<ConfigurationStateBadge state="UpdateAvailable" />);
    expect(screen.getByText('Update Available')).toBeInTheDocument();
  });

  it('renders Drifted state', () => {
    render(<ConfigurationStateBadge state="Drifted" />);
    expect(screen.getByText('Drifted')).toBeInTheDocument();
  });

  it('renders Failed state', () => {
    render(<ConfigurationStateBadge state="Failed" />);
    expect(screen.getByText('Failed')).toBeInTheDocument();
  });

  it('renders None state', () => {
    render(<ConfigurationStateBadge state="None" />);
    expect(screen.getByText('None')).toBeInTheDocument();
  });

  it('renders null as None', () => {
    render(<ConfigurationStateBadge state={null} />);
    expect(screen.getByText('None')).toBeInTheDocument();
  });

  it('renders Unknown state', () => {
    render(<ConfigurationStateBadge state="Unknown" />);
    expect(screen.getByText('Unknown')).toBeInTheDocument();
  });
});
