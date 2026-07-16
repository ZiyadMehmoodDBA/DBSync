import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import type { StatusVariant } from '../../shared/utils/status';
import type { PluginStatus } from './types';

interface StatusConfig {
  variant: StatusVariant;
  icon:    string;
}

const STATUS_CONFIG: Record<PluginStatus, StatusConfig> = {
  Running:     { variant: 'success', icon: '✓' },
  Initialized: { variant: 'warning', icon: '⏳' },
  Loaded:      { variant: 'warning', icon: '⏳' },
  Stopped:     { variant: 'neutral', icon: '■' },
  Failed:      { variant: 'danger',  icon: '✕' },
  Disabled:    { variant: 'neutral', icon: '○' },
};

interface Props { status: PluginStatus }

export function PluginStatusBadge({ status }: Props) {
  const { variant, icon } = STATUS_CONFIG[status] ?? { variant: 'neutral', icon: '' };
  return <StatusBadge status={`${icon} ${status}`} variant={variant} />;
}
