import { Badge } from '../../../components/ui/badge';
import { cn } from '../../../lib/utils';
import type { StatusVariant } from '../../utils/status';

interface Props {
  status: string;
  variant: StatusVariant;
}

const variantClass: Record<StatusVariant, string> = {
  success: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400 border-green-200 dark:border-green-800',
  warning: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400 border-yellow-200 dark:border-yellow-800',
  danger:  'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400 border-red-200 dark:border-red-800',
  neutral: 'bg-neutral-100 text-neutral-700 dark:bg-neutral-800 dark:text-neutral-300 border-neutral-200 dark:border-neutral-700',
};

export function StatusBadge({ status, variant }: Props) {
  return (
    <Badge className={cn('text-xs font-medium', variantClass[variant])}>
      {status}
    </Badge>
  );
}
