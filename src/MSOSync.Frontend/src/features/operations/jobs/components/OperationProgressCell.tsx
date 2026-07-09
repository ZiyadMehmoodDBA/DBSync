import type { OperationStatus } from '@/shared/types/operations';

interface Props {
  status: OperationStatus;
  progressPercent: number | null;
  progressMessage: string | null;
}

export function OperationProgressCell({ status, progressPercent, progressMessage }: Props) {
  // Only show progress bar for Pending and Running states
  if (status !== 'Running' && status !== 'Pending') {
    return <span className="text-xs text-muted-foreground">—</span>;
  }

  const pct = progressPercent ?? 0;

  return (
    <div className="min-w-[100px]">
      <div className="flex items-center justify-between mb-0.5">
        <span className="text-xs font-medium text-foreground">{pct}%</span>
      </div>
      <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
        <div
          className="h-full rounded-full bg-blue-500 transition-all duration-300"
          style={{ width: `${pct}%` }}
        />
      </div>
      {progressMessage && (
        <p className="mt-0.5 truncate text-xs text-muted-foreground max-w-[160px]">
          {progressMessage}
        </p>
      )}
    </div>
  );
}
