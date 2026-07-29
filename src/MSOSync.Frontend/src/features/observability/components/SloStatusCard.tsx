interface SloStatusCardProps {
  label: string;
  value: string;
  target: string;
  met: boolean;
}

export function SloStatusCard({ label, value, target, met }: SloStatusCardProps) {
  return (
    <div className={`rounded-lg border-2 p-4 ${met ? 'border-green-500 bg-green-50' : 'border-red-500 bg-red-50'}`}>
      <div className="text-sm font-medium text-muted-foreground">{label}</div>
      <div className="mt-1 text-3xl font-bold">{value}</div>
      <div className="mt-1 text-sm text-muted-foreground">Target: {target}</div>
      <div className={`mt-2 text-sm font-medium ${met ? 'text-green-700' : 'text-red-700'}`}>
        {met ? '✓ SLO Met' : '✗ SLO Breached'}
      </div>
    </div>
  );
}
