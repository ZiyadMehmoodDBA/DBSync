import { Card, CardContent, CardHeader, CardTitle } from '../../../components/ui/card';
import { Skeleton } from '../../../components/ui/skeleton';
import { cn } from '../../../lib/utils';
import type { LucideIcon } from 'lucide-react';

interface Props {
  title: string;
  value: string | number;
  subtitle?: string;
  icon?: LucideIcon;
  variant?: 'default' | 'success' | 'warning' | 'danger';
  loading?: boolean;
}

const borderVariant: Record<NonNullable<Props['variant']>, string> = {
  default: '',
  success: 'border-green-400 dark:border-green-600',
  warning: 'border-yellow-400 dark:border-yellow-600',
  danger:  'border-red-400 dark:border-red-600',
};

export function SummaryCard({ title, value, subtitle, icon: Icon, variant = 'default', loading = false }: Props) {
  return (
    <Card className={cn('border-l-4', borderVariant[variant])}>
      <CardHeader className="pb-1 pt-4 px-4">
        <CardTitle className="flex items-center gap-2 text-sm font-medium text-neutral-500 dark:text-neutral-400">
          {Icon && <Icon className="h-4 w-4" />}
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="px-4 pb-4">
        {loading ? (
          <Skeleton className="h-8 w-24" />
        ) : (
          <p className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">{value}</p>
        )}
        {subtitle && !loading && (
          <p className="text-xs text-neutral-500 dark:text-neutral-400 mt-1">{subtitle}</p>
        )}
      </CardContent>
    </Card>
  );
}
