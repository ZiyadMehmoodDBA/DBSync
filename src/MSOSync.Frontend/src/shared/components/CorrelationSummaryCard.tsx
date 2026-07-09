import { Badge } from '../../components/ui/badge';
import { Card, CardContent, CardHeader } from '../../components/ui/card';
import type { CorrelationTimelineDto } from '../types/correlation';
import { formatDateTime } from '../utils/date';

const STATUS_COLORS: Record<string, string> = {
  Completed:  'bg-green-100 text-green-800',
  Running:    'bg-blue-100 text-blue-800',
  Failed:     'bg-red-100 text-red-800',
  Pending:    'bg-gray-100 text-gray-600',
  Cancelled:  'bg-gray-100 text-gray-400',
};

const RESULT_COLORS: Record<string, string> = {
  Success:        'bg-green-100 text-green-800',
  PartialSuccess: 'bg-yellow-100 text-yellow-800',
  Failure:        'bg-red-100 text-red-800',
  Cancelled:      'bg-gray-100 text-gray-400',
};

function fmtDate(iso: string | null): string {
  if (!iso) return '—';
  try { return formatDateTime(iso); } catch { return iso; }
}

interface Props {
  timeline: CorrelationTimelineDto;
}

export function CorrelationSummaryCard({ timeline }: Props) {
  return (
    <Card>
      <CardHeader className="pb-2 pt-4 px-4">
        <div className="flex items-start justify-between gap-2">
          <div>
            <p className="text-xs text-muted-foreground font-mono break-all">{timeline.correlationId}</p>
            {timeline.operationType && (
              <p className="text-base font-semibold mt-0.5">{timeline.operationType}</p>
            )}
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {timeline.operationStatus && (
              <Badge className={`text-xs ${STATUS_COLORS[timeline.operationStatus] ?? 'bg-gray-100 text-gray-600'}`}>
                {timeline.operationStatus}
              </Badge>
            )}
            {timeline.operationResult && (
              <Badge className={`text-xs ${RESULT_COLORS[timeline.operationResult] ?? 'bg-gray-100 text-gray-600'}`}>
                {timeline.operationResult}
              </Badge>
            )}
          </div>
        </div>
      </CardHeader>
      <CardContent className="px-4 pb-4">
        <div className="grid grid-cols-2 gap-x-6 gap-y-1 text-xs sm:grid-cols-3">
          <div>
            <p className="text-muted-foreground">Started</p>
            <p>{fmtDate(timeline.startedAt)}</p>
          </div>
          <div>
            <p className="text-muted-foreground">Completed</p>
            <p>{fmtDate(timeline.completedAt)}</p>
          </div>
          {timeline.duration && (
            <div>
              <p className="text-muted-foreground">Duration</p>
              <p>{timeline.duration}</p>
            </div>
          )}
          {timeline.initiatedBy && (
            <div>
              <p className="text-muted-foreground">Initiated by</p>
              <p>{timeline.initiatedBy}</p>
            </div>
          )}
          <div>
            <p className="text-muted-foreground">Events</p>
            <p>{timeline.totalEventCount}</p>
          </div>
        </div>

        {/* Entity chips */}
        {timeline.entityChips.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1.5">
            {timeline.entityChips.map((chip, i) => (
              <Badge key={i} variant="outline" className="text-xs">
                {chip.entityType}: {chip.displayLabel ?? chip.entityId}
              </Badge>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
