# Task 16: Activity Correlation Tab + CorrelationTimeline Component

**Epic:** 12C System Administration Center
**Depends on:** Task 12 (route `/operations/activity` registered pointing to `AuditPage`), backend `GET /api/v1/audit/correlation/{id}` and `GET /api/v1/audit/correlation/search` endpoints
**Blocks:** Nothing — additive to existing AuditPage

---

## Goal

Add a "Correlation" tab to the existing `AuditPage`. The tab contains a search bar, a `CorrelationSummaryCard`, and a phased vertical timeline. URL param `?correlationId=xxx` pre-fills and auto-loads. Export to JSON or Markdown supported via dropdown.

---

## Step 1 — Read AuditPage.tsx fully

- [ ] Open `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`. Note:
  - The Tabs component import path (shadcn `@/shared/components/ui/tabs` or similar)
  - The existing tab values (e.g., `"log"`, `"insights"`)
  - How URL search params are read (if any `useSearchParams` usage exists)
  - The exact component file path — confirm it is `AuditPage.tsx` not `AuditLogPage.tsx`

---

## Step 2 — Create correlation types file

- [ ] Create `src/MSOSync.Frontend/src/shared/types/correlation.ts`:

```typescript
export interface EntityChipDto {
  entityType: string;
  entityId: string;
  displayLabel: string | null;
}

export interface CorrelationEventDto {
  auditId: number;
  occurredAt: string;
  durationSincePrevious: string | null;
  actionName: string;
  summary: string;
  actorUsername: string | null;
  category: string;
  severity: string;
  entityType: string | null;
  entityId: string | null;
  deepLink: string | null;
}

export interface CorrelationPhaseDto {
  phaseName: string;
  events: CorrelationEventDto[];
}

export interface CorrelationTimelineDto {
  correlationId: string;
  operationId: string | null;
  operationType: string | null;
  operationStatus: string | null;
  operationResult: string | null;
  startedAt: string | null;
  completedAt: string | null;
  duration: string | null;
  initiatedBy: string | null;
  entityChips: EntityChipDto[];
  totalEventCount: number;
  isFailedWorkflow: boolean;
  failureSummary: string | null;
  phases: CorrelationPhaseDto[];
}

export interface CorrelationSearchResultDto {
  correlationId: string;
  operationType: string | null;
  operationStatus: string | null;
  startedAt: string | null;
  totalEventCount: number;
  initiatedBy: string | null;
}
```

---

## Step 3 — Add correlation fetch functions to audit.ts

- [ ] Open `src/MSOSync.Frontend/src/shared/api/audit.ts`. At the bottom, add the following exports (keep all existing exports unchanged):

```typescript
import type {
  CorrelationTimelineDto,
  CorrelationSearchResultDto,
} from '../types/correlation';

export async function fetchCorrelationTimeline(
  correlationId: string
): Promise<CorrelationTimelineDto> {
  return apiFetch(`/api/v1/audit/correlation/${encodeURIComponent(correlationId)}`);
}

export async function searchCorrelations(
  params: Record<string, string>
): Promise<CorrelationSearchResultDto[]> {
  const qs = new URLSearchParams(params).toString();
  return apiFetch(`/api/v1/audit/correlation/search${qs ? `?${qs}` : ''}`);
}

export const correlationKeys = {
  timeline: (id: string) => ['correlation', 'timeline', id] as const,
  search: (params: Record<string, string>) => ['correlation', 'search', params] as const,
};
```

---

## Step 4 — Create useCorrelationTimeline.ts hook

- [ ] Create `src/MSOSync.Frontend/src/shared/hooks/useCorrelationTimeline.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import {
  fetchCorrelationTimeline,
  searchCorrelations,
  correlationKeys,
} from '../api/audit';

export function useCorrelationTimeline(correlationId: string) {
  return useQuery({
    queryKey: correlationKeys.timeline(correlationId),
    queryFn: () => fetchCorrelationTimeline(correlationId),
    staleTime: 30_000,
    enabled: correlationId.trim().length > 0,
  });
}

export function useCorrelationSearch(params: Record<string, string>) {
  const hasParams = Object.values(params).some((v) => v.trim().length > 0);
  return useQuery({
    queryKey: correlationKeys.search(params),
    queryFn: () => searchCorrelations(params),
    staleTime: 30_000,
    enabled: hasParams,
  });
}
```

---

## Step 5 — Create CorrelationSummaryCard component

- [ ] Create `src/MSOSync.Frontend/src/shared/components/CorrelationSummaryCard.tsx`:

```typescript
import { Badge } from '@/shared/components/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/components/ui/card';
import type { CorrelationTimelineDto } from '@/shared/types/correlation';
import { format, parseISO } from 'date-fns';

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
  try { return format(parseISO(iso), 'MMM d, yyyy HH:mm:ss'); } catch { return iso; }
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
```

---

## Step 6 — Create CorrelationTimeline component

- [ ] Create `src/MSOSync.Frontend/src/shared/components/CorrelationTimeline.tsx`:

```typescript
import { useState } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useCorrelationTimeline } from '@/shared/hooks/useCorrelationTimeline';
import { CorrelationSummaryCard } from './CorrelationSummaryCard';
import { Button } from '@/shared/components/ui/button';
import { Badge } from '@/shared/components/ui/badge';
import { Input } from '@/shared/components/ui/input';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/shared/components/ui/dropdown-menu';
import { ChevronDown, ChevronRight, Download, AlertTriangle } from 'lucide-react';
import { format, parseISO } from 'date-fns';
import { apiFetch } from '@/shared/api/client';
import type { CorrelationPhaseDto, CorrelationEventDto } from '@/shared/types/correlation';

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

function relativeTime(iso: string): string {
  try {
    const diffMs = Date.now() - new Date(iso).getTime();
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return `${diffSec}s ago`;
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin}m ago`;
    return `${Math.floor(diffMin / 60)}h ago`;
  } catch {
    return iso;
  }
}

function absoluteTime(iso: string): string {
  try { return format(parseISO(iso), 'MMM d HH:mm:ss'); } catch { return iso; }
}

// --- Phase component ---

function PhaseSection({ phase }: { phase: CorrelationPhaseDto }) {
  const [open, setOpen] = useState(true);

  // Determine outcome indicator based on events
  const hasFailed = phase.events.some(
    (e) => e.severity === 'Error' || e.severity === 'Critical'
  );
  const hasWarning = phase.events.some((e) => e.severity === 'Warning');

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
          {hasFailed && <span className="text-destructive text-xs">✗ Failed</span>}
          {!hasFailed && hasWarning && <span className="text-yellow-600 text-xs">⚠ Warning</span>}
          {!hasFailed && !hasWarning && <span className="text-green-600 text-xs">✓</span>}
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
            title={absoluteTime(event.occurredAt)}
          >
            {relativeTime(event.occurredAt)}
          </span>
          {event.deepLink && (
            <Button
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              onClick={() => navigate(event.deepLink!)}
            >
              → Open
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

  async function handleExport(format: 'json' | 'markdown') {
    if (!activeId) return;
    const blob = await apiFetch<Blob>(
      `/api/v1/audit/correlation/${encodeURIComponent(activeId)}/export?format=${format}`,
      { responseType: 'blob' } as never
    );
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `correlation-${activeId}.${format === 'json' ? 'json' : 'md'}`;
    a.click();
    URL.revokeObjectURL(url);
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
              <DropdownMenuItem onClick={() => handleExport('json')}>
                Export as JSON
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => handleExport('markdown')}>
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
```

Note on the export handler: the `apiFetch` call for the export may need adjustment if `apiFetch` does not support `responseType: 'blob'`. In that case, replace with a raw `fetch` call:

```typescript
async function handleExport(fmt: 'json' | 'markdown') {
  if (!activeId) return;
  const token = getAuthToken(); // use whatever auth token accessor exists in the project
  const resp = await fetch(
    `/api/v1/audit/correlation/${encodeURIComponent(activeId)}/export?format=${fmt}`,
    { headers: { Authorization: `Bearer ${token}` } }
  );
  const blob = await resp.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `correlation-${activeId}.${fmt === 'json' ? 'json' : 'md'}`;
  a.click();
  URL.revokeObjectURL(url);
}
```

Check how other mutations get the auth token in the project (it may be added by the fetch interceptor automatically, making explicit header unnecessary).

---

## Step 7 — Modify AuditPage.tsx to add Correlation tab

- [ ] Open `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`.

- [ ] Add the import at the top:

```typescript
import { CorrelationTimeline } from '@/shared/components/CorrelationTimeline';
```

- [ ] Find the `<TabsList>` block. Add a new `TabsTrigger` between the Log trigger and the Insights trigger (or at the end if Insights does not exist):

```typescript
<TabsTrigger value="correlation">Correlation</TabsTrigger>
```

- [ ] After the existing `TabsContent` blocks, add:

```typescript
<TabsContent value="correlation" className="mt-4">
  <CorrelationTimeline />
</TabsContent>
```

- [ ] If the `Tabs` component uses a `defaultValue` prop, change it to `"log"` (it should already be "log"). Do not change the default.

---

## Step 8 — Verify URL param pre-fill works end to end

When navigating from `OverviewActivityFeed` (Task 13) or `JobsPage` (Task 14) to `/operations/activity?correlationId=xxx`, the `CorrelationTimeline` component reads `correlationId` from `useSearchParams()` on mount, sets it as `activeId`, and triggers the query automatically.

- [ ] Confirm the `initialId` value in step:

```typescript
const initialId = searchParams.get('correlationId') ?? '';
const [activeId, setActiveId] = useState(initialId);
```

This is correct — `useState` reads the URL param on first render.

---

## Step 9 — Build check

- [ ] Run:

```powershell
cd src/MSOSync.Frontend && npm run build 2>&1
```

Common issues:
- `DropdownMenu` import: confirm it exists at `@/shared/components/ui/dropdown-menu`. If not, check if a different component name is used in the project (e.g., `DropdownMenuRoot`).
- `Input` component: confirm it exists at `@/shared/components/ui/input`.
- `apiFetch` generic type parameter: if `apiFetch` does not accept a generic, remove `<Blob>` from the call.

---

## Step 10 — Manual smoke test

- [ ] Navigate to `/operations/activity` → click the "Correlation" tab. Verify empty state shows.
- [ ] Enter a valid correlation ID in the search box and click Load. Verify the summary card and phase sections render.
- [ ] Click a phase header to collapse it — verify it collapses to "(N events)".
- [ ] Navigate from `/operations/jobs` by clicking a row with a correlationId. Verify the Correlation tab auto-loads with the correct timeline.

---

## Step 11 — Commit

- [ ] Stage files:

```powershell
git add src/MSOSync.Frontend/src/shared/types/correlation.ts
git add src/MSOSync.Frontend/src/shared/api/audit.ts
git add src/MSOSync.Frontend/src/shared/hooks/useCorrelationTimeline.ts
git add src/MSOSync.Frontend/src/shared/components/CorrelationTimeline.tsx
git add src/MSOSync.Frontend/src/shared/components/CorrelationSummaryCard.tsx
git add src/MSOSync.Frontend/src/features/audit/AuditPage.tsx
```

- [ ] Commit:

```powershell
git commit -m "feat(12C-16): Correlation tab, CorrelationTimeline, phase sections, URL pre-fill, export"
```
