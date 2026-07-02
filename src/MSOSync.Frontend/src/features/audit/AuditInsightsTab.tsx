import { useState, useMemo } from 'react';
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
  { label: '24h', value: '24h' },
  { label: '7d', value: '7d' },
  { label: '30d', value: '30d' },
  { label: '90d', value: '90d' },
  { label: 'Custom', value: 'custom' },
];

export function AuditInsightsTab() {
  const [preset, setPreset] = useState<DatePreset>('7d');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');

  const { from, to } = useMemo(() => {
    if (preset === 'custom') {
      return {
        from: customFrom ? new Date(customFrom).toISOString() : '',
        to: customTo ? new Date(customTo).toISOString() : '',
      };
    }
    return presetToRange(preset);
  }, [preset, customFrom, customTo]);

  const enabled = Boolean(from && to);

  const {
    data: summary,
    isLoading,
    error,
  } = useQuery({
    queryKey: queryKeys.auditSummary(from, to),
    queryFn: () => getAuditSummary(from, to),
    enabled,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });

  const activityOptions: ApexOptions = {
    chart: { type: 'area', toolbar: { show: false }, animations: { enabled: false } },
    stroke: { curve: 'smooth' },
    xaxis: {
      categories: summary?.byDay.map((d) => d.date) ?? [],
      type: 'category',
      labels: { rotate: -45, rotateAlways: false },
    },
    tooltip: { x: { show: true } },
    fill: { type: 'gradient' },
  };

  const failedOptions: ApexOptions = {
    chart: { type: 'area', toolbar: { show: false }, animations: { enabled: false } },
    colors: ['#ef4444'],
    stroke: { curve: 'smooth' },
    xaxis: {
      categories: summary?.byDay.map((d) => d.date) ?? [],
      type: 'category',
      labels: { rotate: -45 },
    },
    tooltip: { x: { show: true } },
    fill: { type: 'gradient' },
  };

  const topUsersOptions: ApexOptions = {
    chart: { type: 'bar', toolbar: { show: false } },
    plotOptions: { bar: { horizontal: true } },
    xaxis: { categories: summary?.byUser.map((u) => u.username) ?? [] },
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
            <StatCard label="Total Actions" value={summary.totalActions} />
            <StatCard
              label="Failed Operations"
              value={summary.failedOperations}
              color="text-red-600"
            />
            <StatCard
              label="Permission Changes"
              value={summary.permissionChanges}
              color="text-amber-600"
            />
          </div>

          {/* Activity Timeline */}
          <ChartCard title="Activity Timeline">
            <ReactApexChart
              type="area"
              height={220}
              options={activityOptions}
              series={[{ name: 'Actions', data: summary.byDay.map((d) => d.total) }]}
            />
          </ChartCard>

          {/* Failed Actions Timeline */}
          <ChartCard title="Failed Actions Timeline">
            <ReactApexChart
              type="area"
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
                series={[
                  { name: 'Changes', data: summary.topParameters.map((p) => p.count) },
                ]}
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
