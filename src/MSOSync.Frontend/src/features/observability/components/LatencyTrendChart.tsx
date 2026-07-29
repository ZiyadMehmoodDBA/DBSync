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
} from 'recharts';

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
        <Tooltip formatter={(v: number) => [`${v}ms`, 'P99 Latency']} />
        <Legend />
        <ReferenceLine
          y={targetMs}
          stroke="red"
          strokeDasharray="4 4"
          label={{ value: `SLO ${targetMs}ms`, position: 'right', fontSize: 11, fill: 'red' }}
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
