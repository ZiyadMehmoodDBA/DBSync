# Task 4: Audit Visualizations

**Part of:** Epic 11D — Export + Audit Intelligence  
**Spec:** `docs/superpowers/specs/2026-07-01-epic11d-export-audit-intelligence-design.md`  
**Depends on:** Task 2 (backend `GET /api/v1/audit/summary`), Task 3 (ExportMenu already in AuditPage)

## Files

**Create:**
- `src/MSOSync.Frontend/src/shared/types/audit-summary.ts`
- `src/MSOSync.Frontend/src/features/audit/AuditInsightsTab.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/shared/api/audit.ts` (add `getAuditSummary`)
- `src/MSOSync.Frontend/src/shared/queryKeys.ts` (add `auditSummary` key)
- `src/MSOSync.Frontend/src/shared/types/index.ts` (re-export `audit-summary` types)
- `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx` (add Tabs: Log | Insights)

## Context

### ApexCharts already installed

`package.json` contains:
```json
"apexcharts": "^5.15.2",
"react-apexcharts": "^2.1.1"
```

Import in components:
```typescript
import ReactApexChart from 'react-apexcharts';
import type { ApexOptions } from 'apexcharts';
```

### AuditPage.tsx after Task 3

```typescript
// Contains: <ExportMenu ...> next to heading, AuditFilters, AuditGrid
// Task 4 must restructure this into Tabs with "Log" and "Insights"
```

### Backend response shape (from Task 2)

```json
{
  "totalActions": 1234,
  "failedOperations": 45,
  "permissionChanges": 12,
  "byDay": [{ "date": "2026-06-24", "total": 100, "failed": 5 }],
  "byUser": [{ "username": "alice", "count": 300 }],
  "byEntityType": [{ "entityType": "SyncNode", "count": 450 }],
  "topParameters": [{ "parameterName": "RetryInterval", "count": 50 }]
}
```

### shadcn Tabs

Check `src/MSOSync.Frontend/src/components/ui/tabs.tsx`. If it does NOT exist, install it:
```pwsh
cd src/MSOSync.Frontend
npx shadcn@latest add tabs --yes
cd ../..
```

If it DOES exist, import directly from `../../components/ui/tabs`.

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword
- All imports relative (no `@/` aliases)
- No new npm packages
- Date presets compute absolute `from`/`to` ISO 8601 strings on the client and pass them as query params
- `ByDay` zero-filled entries must render correctly (they appear as 0 on the chart, not gaps)
- `AuditPage` Log tab must be functionally identical to Task 3's version — only wrapped in `<TabsContent value="log">`

---

- [ ] **Step 1: Create audit-summary types**

```typescript
// src/MSOSync.Frontend/src/shared/types/audit-summary.ts
export interface DayBucket {
  date: string;   // "2026-07-01" — DateOnly serialized by .NET
  total: number;
  failed: number;
}

export interface UserBucket {
  username: string;
  count: number;
}

export interface EntityTypeBucket {
  entityType: string;
  count: number;
}

export interface ParameterBucket {
  parameterName: string;
  count: number;
}

export interface AuditSummaryDto {
  totalActions: number;
  failedOperations: number;
  permissionChanges: number;
  byDay: DayBucket[];
  byUser: UserBucket[];
  byEntityType: EntityTypeBucket[];
  topParameters: ParameterBucket[];
}

export type DatePreset = '24h' | '7d' | '30d' | '90d' | 'custom';
```

- [ ] **Step 2: Re-export from types/index.ts**

Open `src/MSOSync.Frontend/src/shared/types/index.ts`. Add at the end:

```typescript
export * from './audit-summary';
```

- [ ] **Step 3: Add auditSummary query key**

Open `src/MSOSync.Frontend/src/shared/queryKeys.ts`. Add inside the `queryKeys` object, after the existing `auditLog` key:

```typescript
auditSummary: (from: string, to: string) => ['audit', 'summary', from, to] as const,
```

- [ ] **Step 4: Add getAuditSummary to audit.ts**

Open `src/MSOSync.Frontend/src/shared/api/audit.ts`. It currently has `getAuditLog`. Add:

```typescript
import type { AuditSummaryDto } from '../types/audit-summary';

export async function getAuditSummary(from: string, to: string): Promise<AuditSummaryDto> {
  const { data } = await client.get<AuditSummaryDto>('/audit/summary', {
    params: { from, to },
  });
  return data;
}
```

Make sure `client` is already imported at the top (it should be, since `getAuditLog` already uses it).

- [ ] **Step 5: Verify TypeScript compiles — no errors**

```pwsh
cd src/MSOSync.Frontend
npx tsc --noEmit 2>&1 | Select-Object -Last 15
cd ../..
```

Expected: no errors.

- [ ] **Step 6: Check and install shadcn Tabs if needed**

```pwsh
Test-Path "src/MSOSync.Frontend/src/components/ui/tabs.tsx"
```

If output is `False`:
```pwsh
cd src/MSOSync.Frontend
npx shadcn@latest add tabs --yes
cd ../..
```

If output is `True`: skip this step.

- [ ] **Step 7: Create AuditInsightsTab**

```typescript
// src/MSOSync.Frontend/src/features/audit/AuditInsightsTab.tsx
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import ReactApexChart from 'react-apexcharts';
import type { ApexOptions } from 'apexcharts';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { getAuditSummary } from '../../shared/api/audit';
import { queryKeys } from '../../shared/queryKeys';
import type { DatePreset } from '../../shared/types/audit-summary';

function presetToRange(preset: DatePreset): { from: string; to: string } {
  const to = new Date();
  const from = new Date();
  if (preset === '24h') from.setHours(from.getHours() - 24);
  else if (preset === '7d') from.setDate(from.getDate() - 7);
  else if (preset === '30d') from.setDate(from.getDate() - 30);
  else if (preset === '90d') from.setDate(from.getDate() - 90);
  return { from: from.toISOString(), to: to.toISOString() };
}

const PRESETS: { label: string; value: DatePreset }[] = [
  { label: '24h',  value: '24h' },
  { label: '7d',   value: '7d' },
  { label: '30d',  value: '30d' },
  { label: '90d',  value: '90d' },
  { label: 'Custom', value: 'custom' },
];

export function AuditInsightsTab() {
  const [preset, setPreset] = useState<DatePreset>('7d');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo]   = useState('');

  const { from, to } = preset === 'custom'
    ? { from: customFrom ? new Date(customFrom).toISOString() : '',
        to:   customTo   ? new Date(customTo).toISOString()   : '' }
    : presetToRange(preset);

  const enabled = Boolean(from && to);

  const { data: summary, isLoading, error } = useQuery({
    queryKey: queryKeys.auditSummary(from, to),
    queryFn:  () => getAuditSummary(from, to),
    enabled,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });

  const activityOptions: ApexOptions = {
    chart:  { type: 'line', toolbar: { show: false }, animations: { enabled: false } },
    stroke: { curve: 'smooth' },
    xaxis:  { categories: summary?.byDay.map((d) => d.date) ?? [], type: 'category',
               labels: { rotate: -45, rotateAlways: false } },
    tooltip: { x: { show: true } },
  };

  const failedOptions: ApexOptions = {
    chart:  { type: 'line', toolbar: { show: false }, animations: { enabled: false } },
    colors: ['#ef4444'],
    stroke: { curve: 'smooth' },
    xaxis:  { categories: summary?.byDay.map((d) => d.date) ?? [], type: 'category',
               labels: { rotate: -45 } },
    tooltip: { x: { show: true } },
  };

  const topUsersOptions: ApexOptions = {
    chart:       { type: 'bar', toolbar: { show: false } },
    plotOptions: { bar: { horizontal: true } },
    xaxis:       { categories: summary?.byUser.map((u) => u.username) ?? [] },
  };

  const entityTypeOptions: ApexOptions = {
    chart: { type: 'bar', toolbar: { show: false } },
    xaxis: { categories: summary?.byEntityType.map((e) => e.entityType) ?? [] },
  };

  const topParamsOptions: ApexOptions = {
    chart: { type: 'bar', toolbar: { show: false } },
    xaxis: { categories: summary?.topParameters.map((p) => p.parameterName) ?? [] },
  };

  return (
    <div className="flex flex-col gap-6 pt-4">
      {/* Date range controls */}
      <div className="flex flex-wrap items-center gap-2">
        {PRESETS.map((p) => (
          <Button
            key={p.value}
            size="sm"
            variant={preset === p.value ? 'default' : 'outline'}
            onClick={() => setPreset(p.value)}
          >
            {p.label}
          </Button>
        ))}
        {preset === 'custom' && (
          <div className="flex items-center gap-2">
            <Input
              type="date"
              className="w-36"
              value={customFrom}
              onChange={(e) => setCustomFrom(e.target.value)}
            />
            <span className="text-sm text-muted-foreground">to</span>
            <Input
              type="date"
              className="w-36"
              value={customTo}
              onChange={(e) => setCustomTo(e.target.value)}
            />
          </div>
        )}
      </div>

      {!enabled && (
        <p className="text-sm text-muted-foreground">Select a date range to load insights.</p>
      )}
      {enabled && isLoading && (
        <p className="text-sm text-muted-foreground">Loading…</p>
      )}
      {enabled && error && (
        <p className="text-sm text-destructive">Failed to load audit summary.</p>
      )}

      {summary && (
        <>
          {/* Stat cards */}
          <div className="grid grid-cols-3 gap-4">
            <StatCard label="Total Actions"     value={summary.totalActions} />
            <StatCard label="Failed Operations" value={summary.failedOperations} color="text-red-600" />
            <StatCard label="Permission Changes" value={summary.permissionChanges} color="text-amber-600" />
          </div>

          {/* Activity Timeline */}
          <ChartCard title="Activity Timeline">
            <ReactApexChart
              type="line"
              height={220}
              options={activityOptions}
              series={[{ name: 'Actions', data: summary.byDay.map((d) => d.total) }]}
            />
          </ChartCard>

          {/* Failed Actions Timeline */}
          <ChartCard title="Failed Actions Timeline">
            <ReactApexChart
              type="line"
              height={220}
              options={failedOptions}
              series={[{ name: 'Failed', data: summary.byDay.map((d) => d.failed) }]}
            />
          </ChartCard>

          {/* Top Users */}
          {summary.byUser.length > 0 && (
            <ChartCard title="Top Users">
              <ReactApexChart
                type="bar"
                height={Math.max(200, summary.byUser.length * 30)}
                options={topUsersOptions}
                series={[{ name: 'Actions', data: summary.byUser.map((u) => u.count) }]}
              />
            </ChartCard>
          )}

          {/* Entity Types */}
          {summary.byEntityType.length > 0 && (
            <ChartCard title="Entity Types">
              <ReactApexChart
                type="bar"
                height={220}
                options={entityTypeOptions}
                series={[{ name: 'Count', data: summary.byEntityType.map((e) => e.count) }]}
              />
            </ChartCard>
          )}

          {/* Top Parameters */}
          {summary.topParameters.length > 0 && (
            <ChartCard title="Top Parameters">
              <ReactApexChart
                type="bar"
                height={220}
                options={topParamsOptions}
                series={[{ name: 'Changes', data: summary.topParameters.map((p) => p.count) }]}
              />
            </ChartCard>
          )}
        </>
      )}
    </div>
  );
}

function StatCard({
  label,
  value,
  color = 'text-foreground',
}: {
  label: string;
  value: number;
  color?: string;
}) {
  return (
    <div className="rounded-lg border bg-card p-4 shadow-sm">
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className={`text-3xl font-bold ${color}`}>{value.toLocaleString()}</p>
    </div>
  );
}

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-lg border bg-card p-4 shadow-sm">
      <h3 className="mb-2 text-sm font-medium text-muted-foreground">{title}</h3>
      {children}
    </div>
  );
}
```

- [ ] **Step 8: Restructure AuditPage to add Tabs**

Replace the entire `AuditPage.tsx` (which after Task 3 has ExportMenu + AuditFilters + AuditGrid):

```typescript
// src/MSOSync.Frontend/src/features/audit/AuditPage.tsx
import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '../../components/ui/tabs';
import { AuditFilters } from './AuditFilters';
import { AuditGrid } from './AuditGrid';
import { AuditInsightsTab } from './AuditInsightsTab';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useAuditLog } from './hooks';

const defaultFilter: AuditFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function AuditPage() {
  const [filter, setFilter] = useState<AuditFilter>(defaultFilter);
  const { data } = useAuditLog(filter);   // cache-shared with AuditGrid

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Audit</h1>
      <Tabs defaultValue="log">
        <TabsList>
          <TabsTrigger value="log">Log</TabsTrigger>
          <TabsTrigger value="insights">Insights</TabsTrigger>
        </TabsList>
        <TabsContent value="log">
          <div className="flex flex-col gap-4">
            <div className="flex items-center justify-between pt-2">
              <AuditFilters onFilter={setFilter} />
              <ExportMenu
                resource="audit"
                currentData={(data?.data ?? []) as Record<string, unknown>[]}
                queryParams={filter as Record<string, string | number | boolean | undefined>}
              />
            </div>
            <AuditGrid filter={filter} onFilterChange={setFilter} />
          </div>
        </TabsContent>
        <TabsContent value="insights">
          <AuditInsightsTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
```

- [ ] **Step 9: TypeScript check**

```pwsh
cd src/MSOSync.Frontend
npx tsc --noEmit 2>&1 | Select-Object -Last 20
cd ../..
```

Expected: no errors. If ApexCharts types complain, install the types package: `npm install --save-dev @types/apexcharts` (though `apexcharts` bundles its own types in v5).

- [ ] **Step 10: Full build**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror 2>&1 | Select-Object -Last 5
```

Expected: build clean.

- [ ] **Step 11: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/shared/types/audit-summary.ts `
  src/MSOSync.Frontend/src/shared/types/index.ts `
  src/MSOSync.Frontend/src/shared/queryKeys.ts `
  src/MSOSync.Frontend/src/shared/api/audit.ts `
  src/MSOSync.Frontend/src/features/audit/AuditInsightsTab.tsx `
  src/MSOSync.Frontend/src/features/audit/AuditPage.tsx

git commit -m "feat: add Audit Insights tab with 8 ApexCharts visualizations

AuditPage gains Log/Insights tabs. Insights shows date preset picker (24h/7d/30d/90d/Custom),
3 stat cards, Activity Timeline, Failed Actions Timeline, Top Users, Entity Types, Top Parameters."
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Tests: TypeScript: no errors, Build: clean
Concerns: <none or list>
```
