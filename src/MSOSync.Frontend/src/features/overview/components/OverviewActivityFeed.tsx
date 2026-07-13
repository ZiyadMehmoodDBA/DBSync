import { useNavigate } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { OverviewEventDto } from '@/shared/types/system';

const CATEGORY_COLORS: Record<string, string> = {
  Registration:  'bg-purple-100 text-purple-800',
  Lifecycle:     'bg-blue-100 text-blue-800',
  Configuration: 'bg-green-100 text-green-800',
  Operation:     'bg-orange-100 text-orange-800',
  Security:      'bg-red-100 text-red-800',
  System:        'bg-gray-100 text-gray-700',
};

function relativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime();
  const diffSec = Math.floor(diffMs / 1000);
  if (diffSec < 60) return `${diffSec}s ago`;
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  return `${diffHr}h ago`;
}

interface Props {
  events: OverviewEventDto[];
}

export function OverviewActivityFeed({ events }: Props) {
  const navigate = useNavigate();
  const top10 = events.slice(0, 10);

  if (top10.length === 0) {
    return (
      <p className="text-sm text-muted-foreground py-4">No recent activity.</p>
    );
  }

  return (
    <div className="divide-y rounded-lg border">
      {top10.map((ev) => (
        <div key={ev.eventId} className="flex items-start gap-3 px-4 py-3">
          <Badge
            className={`mt-0.5 shrink-0 text-xs ${CATEGORY_COLORS[ev.category] ?? CATEGORY_COLORS['System']}`}
          >
            {ev.category}
          </Badge>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm">{ev.summary}</p>
            {ev.nodeId && (
              <p className="text-xs text-muted-foreground">Node: {ev.nodeId}</p>
            )}
          </div>
          <div className="flex shrink-0 items-center gap-2">
            <span className="text-xs text-muted-foreground" title={ev.occurredAt}>
              {relativeTime(ev.occurredAt)}
            </span>
            {ev.correlationId && (
              <Button
                variant="ghost"
                size="sm"
                className="h-6 px-2 text-xs"
                onClick={() =>
                  navigate(`/operations/activity?correlationId=${encodeURIComponent(ev.correlationId!)}`)
                }
              >
                View →
              </Button>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
