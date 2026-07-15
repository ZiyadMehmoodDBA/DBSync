import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import type { StatusVariant } from '../../shared/utils/status';
import type { PluginStatus } from './types';

function pluginStatusVariant(status: PluginStatus): StatusVariant {
  switch (status) {
    case 'Loaded':     return 'success';
    case 'Failed':     return 'danger';
    case 'Disabled':   return 'neutral';
    case 'Validated':  return 'warning';
    case 'Discovered': return 'warning';
    default:           return 'neutral';
  }
}

interface Props { status: PluginStatus }

export function PluginStatusBadge({ status }: Props) {
  return <StatusBadge status={status} variant={pluginStatusVariant(status)} />;
}
