import { Activity, AlertTriangle, HelpCircle, WifiOff } from 'lucide-react';
import type { ConnectivityStatusName } from '../../types/lifecycle';
import { cn } from '../../../lib/utils';

const META: Record<ConnectivityStatusName, { icon: typeof Activity; className: string }> = {
  Unknown:     { icon: HelpCircle,    className: 'bg-neutral-100 text-neutral-700 dark:bg-neutral-800 dark:text-neutral-300' },
  Reachable:   { icon: Activity,      className: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400' },
  Degraded:    { icon: AlertTriangle, className: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400' },
  Unreachable: { icon: WifiOff,       className: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400' },
};

export function ConnectivityBadge({
  status, reason,
}: { status: ConnectivityStatusName; reason?: string | null }) {
  const meta = META[status] ?? META.Unknown;
  const Icon = meta.icon;
  return (
    <span
      title={reason ?? undefined}
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        meta.className,
      )}
    >
      <Icon className="h-3 w-3" aria-hidden />
      {status}
    </span>
  );
}
