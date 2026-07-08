import { render, screen } from '@testing-library/react';
import { ConfigurationDiffViewer } from '../../../components/ui/ConfigurationDiffViewer';
import { describe, it, expect } from 'vitest';
import type { ConfigurationSettings } from '../types';

const base: ConfigurationSettings = {
  heartbeatIntervalSeconds: 30,
  transportMode: 'Push',
  maxRetryAttempts: 3,
  retryBackoffSeconds: 60,
  batchSizeLimit: 1000,
  minimumAgentVersion: null,
  featureFlags: { enableBulkApply: true },
  channelIds: [],
  routerIds: [],
  triggerIds: [],
};

describe('ConfigurationDiffViewer', () => {
  it('shows no differences when identical', () => {
    render(<ConfigurationDiffViewer settings1={base} settings2={base} />);
    expect(screen.getByText(/no differences/i)).toBeInTheDocument();
  });

  it('shows field name when value differs', () => {
    const modified = { ...base, heartbeatIntervalSeconds: 60 };
    render(<ConfigurationDiffViewer settings1={base} settings2={modified} />);
    expect(screen.getByText('heartbeatIntervalSeconds')).toBeInTheDocument();
  });
});
