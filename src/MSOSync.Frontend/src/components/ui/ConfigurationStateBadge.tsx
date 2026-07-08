import type { ConfigurationState } from '../../features/configuration/types';
import { cn } from '../../lib/utils';

interface Props {
  state: ConfigurationState | null | undefined;
  className?: string;
}

const STATE_CONFIG: Record<ConfigurationState, { label: string; icon: string; className: string }> = {
  Current:         { label: 'Current',         icon: '✓', className: 'text-green-700 bg-green-50 border-green-200' },
  UpdateAvailable: { label: 'Update Available', icon: '↑', className: 'text-yellow-700 bg-yellow-50 border-yellow-200' },
  Applying:        { label: 'Applying',         icon: '⟳', className: 'text-blue-700 bg-blue-50 border-blue-200' },
  Drifted:         { label: 'Drifted',          icon: '!', className: 'text-orange-700 bg-orange-50 border-orange-200' },
  Failed:          { label: 'Failed',           icon: '✗', className: 'text-red-700 bg-red-50 border-red-200' },
  None:            { label: 'None',             icon: '–', className: 'text-gray-500 bg-gray-50 border-gray-200' },
  Unknown:         { label: 'Unknown',          icon: '?', className: 'text-gray-500 bg-gray-50 border-gray-200' },
};

export function ConfigurationStateBadge({ state, className }: Props) {
  const resolved = (state ?? 'None') as ConfigurationState;
  const config = STATE_CONFIG[resolved] ?? STATE_CONFIG.None;

  return (
    <span
      role="status"
      aria-label={`Configuration state: ${config.label}`}
      className={cn(
        'inline-flex items-center gap-1 px-2 py-0.5 rounded border text-xs font-medium',
        config.className,
        className,
      )}
    >
      <span aria-hidden="true">{config.icon}</span>
      {config.label}
    </span>
  );
}
