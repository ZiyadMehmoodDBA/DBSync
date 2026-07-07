import { Wrench } from 'lucide-react';

export function MaintenanceBadge({
  active, reason,
}: { active: boolean; reason?: string | null }) {
  if (!active) return null;
  return (
    <span
      title={reason ?? undefined}
      className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-900/30 dark:text-amber-400"
    >
      <Wrench className="h-3 w-3" aria-hidden />
      Maintenance
    </span>
  );
}
