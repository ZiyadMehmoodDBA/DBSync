import {
  CheckCircle2, CircleOff, Clock, HardDriveDownload, LifeBuoy, Trash2, XCircle, KeyRound,
} from 'lucide-react';
import type { NodeLifecycleState } from '../../types/lifecycle';
import { cn } from '../../../lib/utils';

// Color + icon + label — state is never encoded by color alone (spec §11.1).
const META: Record<NodeLifecycleState, { icon: typeof CheckCircle2; className: string }> = {
  PendingApproval:     { icon: Clock,             className: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400' },
  PendingRegistration: { icon: KeyRound,           className: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-400' },
  Active:              { icon: CheckCircle2,       className: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400' },
  Recovery:            { icon: LifeBuoy,           className: 'bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-400' },
  Disabled:            { icon: CircleOff,          className: 'bg-neutral-200 text-neutral-700 dark:bg-neutral-800 dark:text-neutral-300' },
  Decommissioning:     { icon: HardDriveDownload,  className: 'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-400' },
  Decommissioned:      { icon: Trash2,             className: 'bg-neutral-100 text-neutral-500 dark:bg-neutral-900 dark:text-neutral-500' },
  Rejected:            { icon: XCircle,            className: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400' },
};

export function LifecycleBadge({ state }: { state: NodeLifecycleState }) {
  const meta = META[state] ?? META.Disabled;
  const Icon = meta.icon;
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        meta.className,
      )}
    >
      <Icon className="h-3 w-3" aria-hidden />
      {state}
    </span>
  );
}
