import ReactApexChart from 'react-apexcharts';
import type { ApexOptions } from 'apexcharts';
import type { WorkerTickDto } from '@/shared/types/system';
import { formatDateTime } from '@/shared/utils/date';

interface Props {
  ticks: WorkerTickDto[];
}

interface ChartDatum {
  x: number;
  y: number;
  isSuccess: boolean;
  startedAt: string;
  trigger: string;
  errorMessage: string | null;
}

export function WorkerTickChart({ ticks }: Props) {
  if (!ticks.length) {
    return (
      <p className="text-xs text-muted-foreground py-2">No tick history available.</p>
    );
  }

  // Show last 100 ticks in chronological order (oldest first = left)
  const sliced = ticks.slice(-100);
  const data: ChartDatum[] = sliced.map((t, i) => ({
    x: i,
    y: t.durationMs ?? 0,
    isSuccess: t.isSuccess,
    startedAt: t.startedAt,
    trigger: t.trigger,
    errorMessage: t.errorMessage,
  }));

  const colors = data.map((d) => (d.isSuccess ? '#22c55e' : '#ef4444'));

  const options: ApexOptions = {
    chart: {
      type: 'bar',
      toolbar: { show: false },
      animations: { enabled: false },
      sparkline: { enabled: false },
    },
    plotOptions: {
      bar: {
        columnWidth: '90%',
        borderRadius: 2,
        distributed: true,
      },
    },
    colors,
    legend: { show: false },
    dataLabels: { enabled: false },
    xaxis: {
      labels: { show: false },
      axisBorder: { show: false },
      axisTicks: { show: false },
    },
    yaxis: {
      labels: { show: false },
    },
    grid: { show: false },
    tooltip: {
      custom: ({ dataPointIndex }: { dataPointIndex: number }) => {
        const d = data[dataPointIndex];
        if (!d) return '';
        const status = d.isSuccess ? '<span style="color:#22c55e">&#10003; Success</span>' : '<span style="color:#ef4444">&#10007; Failed</span>';
        const started = formatDateTime(d.startedAt);
        const dur = d.y != null ? `${d.y}ms` : '—';
        const errorLine = d.errorMessage
          ? `<div style="color:#ef4444;max-width:240px;word-break:break-word">Error: ${d.errorMessage}</div>`
          : '';
        return `
          <div style="padding:8px 10px;font-size:12px;line-height:1.6">
            <div><strong>${status}</strong></div>
            <div>Started: ${started}</div>
            <div>Duration: ${dur}</div>
            <div>Trigger: ${d.trigger}</div>
            ${errorLine}
          </div>
        `;
      },
    },
  };

  const series = [{ name: 'Duration', data: data.map((d) => d.y) }];

  return (
    <div className="h-[80px] w-full overflow-hidden">
      <ReactApexChart
        type="bar"
        height={80}
        options={options}
        series={series}
      />
    </div>
  );
}
