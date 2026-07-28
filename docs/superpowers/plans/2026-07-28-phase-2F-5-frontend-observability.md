# Phase 2F.5 — Frontend Observability UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver an `ObservabilityPage` at `/administration/observability` that shows per-node health scores (0–100, grade A–F), SLO status cards (delivery rate vs target, P99 latency vs target), and a pipeline latency trend chart.

**Architecture:** TanStack Query v5 hooks fetch `/api/health/scores` and `/api/slo/status`. `NodeHealthTable` renders the node list with color-coded grades. `SloStatusCard` renders each SLO metric with pass/fail indicator. A recharts `LineChart` shows P99 latency over a rolling window (fetched from backend if data is available, or shown as a stub when no data).

**Tech Stack:** React 19 / TypeScript / TanStack Query v5 / recharts / shadcn/ui / Tailwind CSS

## Global Constraints

- Prerequisite: 2F.3 complete — `GET /api/health/scores` and `GET /api/slo/status` exist
- React 19 / TanStack Query v5 — no `onSuccess`/`onError` on `useQuery`
- Route: `/administration/observability` — AdminOnly (follow existing admin route guard pattern)
- Grade colors: A=green, B=light-green, C=yellow, D=orange, F=red
- SLO: green border when met, red border when not met
- `git add` by file name only

---

### Task 1: API types + TanStack Query hooks

**Files:**
- Create: `src/hooks/useObservability.ts`

**Interfaces:**
- Consumes: `GET /api/health/scores`, `GET /api/slo/status` (2F.3)
- Produces: `useHealthScores()`, `useSloStatus()` hooks; `NodeHealthScore`, `SloStatus` TypeScript types; `observabilityKeys` queryKey factory

- [ ] **Step 1: Locate existing hook pattern**

```powershell
Get-ChildItem -Recurse -Include "use*.ts","use*.tsx" src/hooks/ | Select-Object -First 3 FullName
```

Read one existing hook to understand the fetch pattern (base URL, auth headers, error handling). Adapt the code below to match.

- [ ] **Step 2: Create useObservability.ts**

```typescript
// src/hooks/useObservability.ts
import { useQuery } from "@tanstack/react-query";

export interface NodeHealthScore {
  nodeId: number;
  nodeName: string;
  score: number;
  grade: "A" | "B" | "C" | "D" | "F";
  connectivityScore: number;
  syncLagScore: number;
  errorRateScore: number;
  heartbeatScore: number;
  computedAt: string;
}

export interface SloStatus {
  deliveryRate: number;
  deliveryRateTarget: number;
  deliveryRateMet: boolean;
  latencyP99Ms: number;
  latencyP99TargetMs: number;
  latencyP99Met: boolean;
  windowStart: string;
  windowEnd: string;
}

export const observabilityKeys = {
  all: ["observability"] as const,
  healthScores: () => [...observabilityKeys.all, "health-scores"] as const,
  sloStatus: () => [...observabilityKeys.all, "slo-status"] as const,
};

async function fetchHealthScores(): Promise<NodeHealthScore[]> {
  const res = await fetch("/api/health/scores");
  if (!res.ok) throw new Error(`Health scores fetch failed: ${res.status}`);
  return res.json();
}

async function fetchSloStatus(): Promise<SloStatus> {
  const res = await fetch("/api/slo/status");
  if (!res.ok) throw new Error(`SLO status fetch failed: ${res.status}`);
  return res.json();
}

export function useHealthScores() {
  return useQuery({
    queryKey: observabilityKeys.healthScores(),
    queryFn: fetchHealthScores,
    refetchInterval: 30_000,
  });
}

export function useSloStatus() {
  return useQuery({
    queryKey: observabilityKeys.sloStatus(),
    queryFn: fetchSloStatus,
    refetchInterval: 60_000,
  });
}
```

Note: if existing hooks use an authenticated `apiClient` or `apiFetch` wrapper, replace `fetch(...)` calls with the appropriate wrapper (read an existing hook to find the pattern).

- [ ] **Step 3: Verify TypeScript**

```powershell
cd src/MSOSync.Frontend; npx tsc --noEmit 2>&1 | Select-Object -Last 5
```

Expected: no type errors in the new file.

- [ ] **Step 4: Commit**

```
git add src/hooks/useObservability.ts
git commit -m "feat(2F.5-T1): add useObservability hook + TypeScript types"
```

---

### Task 2: ObservabilityPage scaffold + nav entry

**Files:**
- Create: `src/pages/administration/ObservabilityPage.tsx`
- Modify: router file (found in Task 1 Step 1 of 2E.6-T4 or via `Get-ChildItem -Recurse -Include "*router*","*routes*" src/`)
- Modify: admin sidebar nav (find via `Get-ChildItem -Recurse -Include "*Sidebar*","*Nav*","*sidebar*","*nav*" src/components/` or similar)

**Interfaces:**
- Consumes: `useHealthScores`, `useSloStatus` (Task 1)
- Produces: AdminOnly route `/administration/observability`, nav entry

- [ ] **Step 1: Locate router and nav files**

```powershell
Get-ChildItem -Recurse -Include "*.tsx" src/ | Where-Object { $_.FullName -like "*administration*" } | Select-Object FullName
```

Read an existing admin page and the router to understand exact route registration pattern.

- [ ] **Step 2: Create ObservabilityPage**

```tsx
// src/pages/administration/ObservabilityPage.tsx
import { useHealthScores, useSloStatus } from "../../hooks/useObservability";
import { NodeHealthTable } from "../../components/observability/NodeHealthTable";
import { SloStatusCard } from "../../components/observability/SloStatusCard";
// Adapt import paths to project structure

export function ObservabilityPage() {
  const { data: healthScores, isLoading: scoresLoading, error: scoresError } = useHealthScores();
  const { data: sloStatus, isLoading: sloLoading } = useSloStatus();

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Observability</h1>
        <span className="text-sm text-muted-foreground">
          Auto-refreshes every 30s
        </span>
      </div>

      {/* SLO Status */}
      <section>
        <h2 className="text-lg font-medium mb-3">SLO Status</h2>
        {sloLoading ? (
          <div className="text-muted-foreground">Loading SLO status...</div>
        ) : sloStatus ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <SloStatusCard
              label="Delivery Rate"
              value={`${(sloStatus.deliveryRate * 100).toFixed(3)}%`}
              target={`≥ ${(sloStatus.deliveryRateTarget * 100).toFixed(1)}%`}
              met={sloStatus.deliveryRateMet}
            />
            <SloStatusCard
              label="P99 Latency"
              value={`${sloStatus.latencyP99Ms.toFixed(0)}ms`}
              target={`≤ ${sloStatus.latencyP99TargetMs}ms`}
              met={sloStatus.latencyP99Met}
            />
          </div>
        ) : (
          <div className="text-muted-foreground">No SLO data available</div>
        )}
      </section>

      {/* Node Health */}
      <section>
        <h2 className="text-lg font-medium mb-3">Node Health Scores</h2>
        {scoresError ? (
          <div className="text-red-500">Failed to load health scores</div>
        ) : (
          <NodeHealthTable scores={healthScores ?? []} loading={scoresLoading} />
        )}
      </section>
    </div>
  );
}
```

- [ ] **Step 3: Register route**

Add to the router (following existing admin route pattern):

```tsx
// Admin-protected routes section:
{ path: "/administration/observability", element: <ObservabilityPage /> }
// or:
<Route path="/administration/observability" element={<ObservabilityPage />} />
```

- [ ] **Step 4: Add nav entry**

In the admin sidebar navigation, add an Observability item following the existing nav item pattern. Example using shadcn/ui:

```tsx
<NavItem
  href="/administration/observability"
  icon={<BarChart2 className="h-4 w-4" />}
  label="Observability"
/>
```

Use whatever icon/nav component pattern already exists. Import the icon from `lucide-react`.

- [ ] **Step 5: Build**

```powershell
cd src/MSOSync.Frontend; npm run build 2>&1 | Select-Object -Last 10
```

Expected: no TypeScript errors, build succeeds.

- [ ] **Step 6: Commit**

```
git add src/pages/administration/ObservabilityPage.tsx <router-file> <nav-file>
git commit -m "feat(2F.5-T2): add ObservabilityPage scaffold + nav entry"
```

---

### Task 3: NodeHealthTable + SloStatusCard components

**Files:**
- Create: `src/components/observability/NodeHealthTable.tsx`
- Create: `src/components/observability/SloStatusCard.tsx`

**Interfaces:**
- Consumes: `NodeHealthScore[]` (Task 1), `SloStatusCard` props
- Produces: `NodeHealthTable({ scores, loading })` — table with color-coded grades; `SloStatusCard({ label, value, target, met })` — card with green/red border

- [ ] **Step 1: Create SloStatusCard**

```tsx
// src/components/observability/SloStatusCard.tsx
interface SloStatusCardProps {
  label: string;
  value: string;
  target: string;
  met: boolean;
}

export function SloStatusCard({ label, value, target, met }: SloStatusCardProps) {
  return (
    <div
      className={`rounded-lg border-2 p-4 ${
        met ? "border-green-500 bg-green-50" : "border-red-500 bg-red-50"
      }`}
    >
      <div className="text-sm font-medium text-muted-foreground">{label}</div>
      <div className="mt-1 text-3xl font-bold">{value}</div>
      <div className="mt-1 text-sm text-muted-foreground">Target: {target}</div>
      <div className={`mt-2 text-sm font-medium ${met ? "text-green-700" : "text-red-700"}`}>
        {met ? "✓ SLO Met" : "✗ SLO Breached"}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Create NodeHealthTable**

```tsx
// src/components/observability/NodeHealthTable.tsx
import type { NodeHealthScore } from "../../hooks/useObservability";

interface NodeHealthTableProps {
  scores: NodeHealthScore[];
  loading: boolean;
}

const GRADE_COLORS: Record<string, string> = {
  A: "bg-green-100 text-green-800",
  B: "bg-lime-100 text-lime-800",
  C: "bg-yellow-100 text-yellow-800",
  D: "bg-orange-100 text-orange-800",
  F: "bg-red-100 text-red-800",
};

export function NodeHealthTable({ scores, loading }: NodeHealthTableProps) {
  if (loading) {
    return <div className="text-muted-foreground">Loading node health scores...</div>;
  }

  if (scores.length === 0) {
    return <div className="text-muted-foreground">No sync nodes found.</div>;
  }

  return (
    <div className="overflow-x-auto rounded-lg border">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b bg-muted/50">
            <th className="p-3 text-left">Node</th>
            <th className="p-3 text-center">Grade</th>
            <th className="p-3 text-right">Score</th>
            <th className="p-3 text-right">Connectivity</th>
            <th className="p-3 text-right">Sync Lag</th>
            <th className="p-3 text-right">Error Rate</th>
            <th className="p-3 text-right">Heartbeat</th>
          </tr>
        </thead>
        <tbody>
          {scores.map((node) => (
            <tr key={node.nodeId} className="border-b hover:bg-muted/30">
              <td className="p-3 font-medium">{node.nodeName}</td>
              <td className="p-3 text-center">
                <span
                  className={`inline-block rounded px-2 py-0.5 text-xs font-bold ${
                    GRADE_COLORS[node.grade] ?? "bg-gray-100 text-gray-800"
                  }`}
                >
                  {node.grade}
                </span>
              </td>
              <td className="p-3 text-right font-mono">{node.score}/100</td>
              <td className="p-3 text-right font-mono">{node.connectivityScore}/40</td>
              <td className="p-3 text-right font-mono">{node.syncLagScore}/30</td>
              <td className="p-3 text-right font-mono">{node.errorRateScore}/20</td>
              <td className="p-3 text-right font-mono">{node.heartbeatScore}/10</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
```

- [ ] **Step 3: Build**

```powershell
cd src/MSOSync.Frontend; npm run build 2>&1 | Select-Object -Last 10
```

Expected: no TypeScript errors.

- [ ] **Step 4: Commit**

```
git add src/components/observability/NodeHealthTable.tsx src/components/observability/SloStatusCard.tsx
git commit -m "feat(2F.5-T3): add NodeHealthTable + SloStatusCard components"
```

---

### Task 4: P99 latency chart (recharts) + component tests

**Files:**
- Create: `src/components/observability/LatencyTrendChart.tsx`
- Modify: `src/pages/administration/ObservabilityPage.tsx` (add chart section)
- Create: `src/components/observability/__tests__/NodeHealthTable.test.tsx`
- Create: `src/components/observability/__tests__/SloStatusCard.test.tsx`

**Interfaces:**
- Consumes: `NodeHealthScore[]` (for mock data in chart), recharts `LineChart`
- Produces: `LatencyTrendChart` — line chart of mock/fetched latency data; passing tests for both components

- [ ] **Step 1: Verify recharts is installed**

```powershell
cd src/MSOSync.Frontend; cat package.json | Select-String "recharts"
```

If recharts is not installed:
```powershell
npm install recharts
```

- [ ] **Step 2: Create LatencyTrendChart**

The chart receives data points as props. When the backend does not provide time-series data (2F.3 returns only current SLO snapshot), show a placeholder message. This component is ready for when a time-series endpoint is added in future phases.

```tsx
// src/components/observability/LatencyTrendChart.tsx
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  ResponsiveContainer,
  Legend,
} from "recharts";

export interface LatencyDataPoint {
  time: string;
  p99Ms: number;
}

interface LatencyTrendChartProps {
  data: LatencyDataPoint[];
  targetMs: number;
}

export function LatencyTrendChart({ data, targetMs }: LatencyTrendChartProps) {
  if (data.length === 0) {
    return (
      <div className="flex h-48 items-center justify-center rounded-lg border text-muted-foreground">
        No latency time-series data available. Upgrade to Enterprise Edition for historical metrics.
      </div>
    );
  }

  return (
    <ResponsiveContainer width="100%" height={240}>
      <LineChart data={data} margin={{ top: 4, right: 16, left: 0, bottom: 4 }}>
        <CartesianGrid strokeDasharray="3 3" />
        <XAxis dataKey="time" tick={{ fontSize: 12 }} />
        <YAxis unit="ms" tick={{ fontSize: 12 }} />
        <Tooltip formatter={(v: number) => [`${v}ms`, "P99 Latency"]} />
        <Legend />
        <ReferenceLine
          y={targetMs}
          stroke="red"
          strokeDasharray="4 4"
          label={{ value: `SLO ${targetMs}ms`, position: "right", fontSize: 11, fill: "red" }}
        />
        <Line
          type="monotone"
          dataKey="p99Ms"
          stroke="#6366f1"
          strokeWidth={2}
          dot={false}
          name="P99 Latency"
        />
      </LineChart>
    </ResponsiveContainer>
  );
}
```

- [ ] **Step 3: Add chart to ObservabilityPage**

Read `src/pages/administration/ObservabilityPage.tsx`. Add the chart section below the SLO cards:

```tsx
import { LatencyTrendChart } from "../../components/observability/LatencyTrendChart";

// Inside ObservabilityPage, after the SLO section:
<section>
  <h2 className="text-lg font-medium mb-3">P99 Latency Trend</h2>
  <LatencyTrendChart
    data={[]}  // empty until time-series endpoint is available
    targetMs={sloStatus?.latencyP99TargetMs ?? 5000}
  />
</section>
```

- [ ] **Step 4: Write component tests**

Find the test runner config (Jest or Vitest). Read an existing test file pattern. The tests below use Vitest + React Testing Library — adapt if the project uses a different setup.

```tsx
// src/components/observability/__tests__/SloStatusCard.test.tsx
import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { SloStatusCard } from "../SloStatusCard";

describe("SloStatusCard", () => {
  it("shows SLO Met when met=true", () => {
    render(<SloStatusCard label="Delivery Rate" value="99.95%" target="≥ 99.9%" met={true} />);
    expect(screen.getByText("✓ SLO Met")).toBeInTheDocument();
  });

  it("shows SLO Breached when met=false", () => {
    render(<SloStatusCard label="P99 Latency" value="6000ms" target="≤ 5000ms" met={false} />);
    expect(screen.getByText("✗ SLO Breached")).toBeInTheDocument();
  });
});
```

```tsx
// src/components/observability/__tests__/NodeHealthTable.test.tsx
import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { NodeHealthTable } from "../NodeHealthTable";
import type { NodeHealthScore } from "../../../hooks/useObservability";

const mockScores: NodeHealthScore[] = [
  {
    nodeId: 1,
    nodeName: "Node Alpha",
    score: 95,
    grade: "A",
    connectivityScore: 40,
    syncLagScore: 30,
    errorRateScore: 20,
    heartbeatScore: 5,
    computedAt: new Date().toISOString(),
  },
];

describe("NodeHealthTable", () => {
  it("renders node name and grade", () => {
    render(<NodeHealthTable scores={mockScores} loading={false} />);
    expect(screen.getByText("Node Alpha")).toBeInTheDocument();
    expect(screen.getByText("A")).toBeInTheDocument();
  });

  it("shows loading message when loading=true", () => {
    render(<NodeHealthTable scores={[]} loading={true} />);
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it("shows empty message when no nodes", () => {
    render(<NodeHealthTable scores={[]} loading={false} />);
    expect(screen.getByText(/no sync nodes/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 5: Run frontend tests**

```powershell
cd src/MSOSync.Frontend; npm test 2>&1 | Select-Object -Last 15
```

Expected: tests pass (or note any setup required to run tests in the environment).

- [ ] **Step 6: Build final**

```powershell
cd src/MSOSync.Frontend; npm run build 2>&1 | Select-Object -Last 10
```

Expected: `Build succeeded` with no TypeScript errors.

- [ ] **Step 7: Commit**

```
git add src/components/observability/LatencyTrendChart.tsx src/components/observability/__tests__/NodeHealthTable.test.tsx src/components/observability/__tests__/SloStatusCard.test.tsx src/pages/administration/ObservabilityPage.tsx
git commit -m "feat(2F.5-T4): add LatencyTrendChart + component tests for ObservabilityPage"
```
