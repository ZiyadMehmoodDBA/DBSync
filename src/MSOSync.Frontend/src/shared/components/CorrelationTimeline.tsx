import { useState } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useCorrelationTimeline } from '../hooks/useCorrelationTimeline';
import { CorrelationSummaryCard } from './CorrelationSummaryCard';
import { Button } from '../../components/ui/button';
import { Badge } from '../../components/ui/badge';
import { Input } from '../../components/ui/input';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '../../components/ui/dropdown-menu';
import { ChevronDown, ChevronRight, Download, AlertTriangle } from 'lucide-react';
import { formatDateTime, formatRelativeTime } from '../utils/date';
import { exportCorrelation } from '../api/audit';
import type { CorrelationPhaseDto, CorrelationEventDto } from '../types/correlation';

// --- Constants ---

const CATEGORY_COLORS: Record<string, string> = {
  Registration:  'bg-purple-100 text-purple-800',
  Lifecycle:     'bg-blue-100 text-blue-800',
  Configuration: 'bg-green-100 text-green-800',
  Operation:     'bg-orange-100 text-orange-800',
  Security:      'bg-red-100 text-red-800',
  System:        'bg-gray-100 text-gray-700',
};

const SEVERITY_INDICATOR: Record<string, string> = {
  Information: '',
  Warning:     '⚠',
  Error:       '✗',
  Critical:    '💥',
};

// --- Phase component ---

function PhaseSection({ phase }: { phase: CorrelationPhaseDto }) {
  const [open, setOpen] = useState(true);

  return (
    <div className="rounded-lg border">
      <button
        className="flex w-full items-center justify-between px-4 py-3 text-left hover:bg-muted/40 transition-colors"
        onClick={() => setOpen((v) => !v)}
      >
        <div className="flex items-center gap-2">
          {open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
          <span className="text-sm font-semibold">{phase.phaseName}</span>
          <Badge variant="outline" className="text-xs h-5 px-1.5">
            {phase.events.length} events
          </Badge>
          {phase.hasErrors && <span className="text-destructive text-xs">✗ Failed</span>}
          {!phase.hasErrors && <span className="text-green-600 text-xs">✓</span>}
        </div>
      </button>

      {open && (
        <div className="border-t">
          {phase.events.map((ev, idx) => (
            <EventRow key={ev.auditId} event={ev} showGap={idx > 0} />
          ))}
        </div>
      )}

      {!open && (
        <div className="px-4 py-2 text-xs text-muted-foreground border-t">
          {phase.events.length} events — click to expand
        </div>
      )}
    </div>
  );
}

// --- Event row ---

function EventRow({
  event,
  showGap,
}: {
  event: CorrelationEventDto;
  showGap: boolean;
}) {
  const navigate = useNavigate();

  return (
    <>
      {showGap && event.durationSincePrevious && (
        <div className="flex justify-center py-0.5">
          <span className="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded-full">
            +{event.durationSincePrevious}
          </span>
        </div>
      )}
      <div className="flex items-start gap-3 px-4 py-3 border-b last:border-b-0 hover:bg-muted/30">
        {/* Category badge */}
        <Badge
          className={`shrink-0 mt-0.5 text-xs ${CATEGORY_COLORS[event.category] ?? CATEGORY_COLORS['System']}`}
        >
          {event.category}
        </Badge>

        {/* Severity + summary */}
        <div className="min-w-0 flex-1">
          <div className="flex items-baseline gap-1">
            {SEVERITY_INDICATOR[event.severity] && (
              <span className="text-sm" title={event.severity}>
                {SEVERITY_INDICATOR[event.severity]}
              </span>
            )}
            <p className="text-sm">{event.summary}</p>
          </div>
          <div className="mt-0.5 flex items-center gap-2 text-xs text-muted-foreground">
            {event.actorUsername && <span>by {event.actorUsername}</span>}
            {event.entityType && event.entityId && (
              <span className="font-mono">{event.entityType}/{event.entityId}</span>
            )}
          </div>
        </div>

        {/* Timestamp + deep link */}
        <div className="flex shrink-0 items-center gap-2">
          <span
            className="text-xs text-muted-foreground cursor-default"
            title={formatDateTime(event.occurredAt)}
          >
            {formatRelativeTime(event.occurredAt)}
          </span>
          {event.deepLink && (
            <Button
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              onClick={() => navigate(event.deepLink!)}
            >
              Open
            </Button>
          )}
        </div>
      </div>
    </>
  );
}

// --- Main component ---

export function CorrelationTimeline() {
  const [searchParams, setSearchParams] = useSearchParams();
  const initialId = searchParams.get('correlationId') ?? '';

  const [inputValue, setInputValue] = useState(initialId);
  const [activeId, setActiveId] = useState(initialId);

  const { data: timeline, isLoading, error } = useCorrelationTimeline(activeId);

  function handleSearch() {
    const id = inputValue.trim();
    if (!id) return;
    setActiveId(id);
    setSearchParams({ correlationId: id }, { replace: true });
  }

  async function handleExport(fmt: 'json' | 'markdown') {
    if (!activeId) return;
    try {
      const blob = await exportCorrelation(activeId, fmt);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `correlation-${activeId}.${fmt === 'json' ? 'json' : 'md'}`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      // Export errors are non-critical — silently ignore
    }
  }

  return (
    <div className="space-y-4">
      {/* Search bar */}
      <div className="flex items-center gap-2">
        <Input
          placeholder="Enter Correlation ID (UUID)…"
          value={inputValue}
          onChange={(e) => setInputValue(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
          className="max-w-md"
        />
        <Button onClick={handleSearch} disabled={!inputValue.trim()}>
          Load
        </Button>
        {timeline && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm" className="ml-auto gap-1">
                <Download className="h-3.5 w-3.5" />
                Export
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onClick={() => { void handleExport('json'); }}>
                Export as JSON
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => { void handleExport('markdown'); }}>
                Export as Markdown
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>

      {/* Loading */}
      {isLoading && (
        <p className="text-sm text-muted-foreground py-4">Loading timeline…</p>
      )}

      {/* Error / not found */}
      {error && !isLoading && (
        <div className="rounded-lg border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm text-destructive">
          Correlation not found or failed to load. Verify the ID and try again.
        </div>
      )}

      {/* Timeline */}
      {timeline && !isLoading && (
        <div className="space-y-4">
          <CorrelationSummaryCard timeline={timeline} />

          {timeline.phases.map((phase) => (
            <PhaseSection key={phase.phaseName} phase={phase} />
          ))}

          {/* Failed workflow banner */}
          {timeline.isFailedWorkflow && (
            <div className="flex items-start gap-3 rounded-lg border border-red-300 bg-red-50 px-4 py-3">
              <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0 text-red-600" />
              <div>
                <p className="text-sm font-semibold text-red-800">Workflow Failed</p>
                {timeline.failureSummary && (
                  <p className="text-xs text-red-700 mt-0.5">{timeline.failureSummary}</p>
                )}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Empty state */}
      {!activeId && !isLoading && (
        <p className="text-sm text-muted-foreground py-8 text-center">
          Enter a Correlation ID above to load the timeline.
        </p>
      )}
    </div>
  );
}
