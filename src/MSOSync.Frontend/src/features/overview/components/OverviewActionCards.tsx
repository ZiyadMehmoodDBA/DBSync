import { useNavigate } from 'react-router-dom';
import { Card, CardHeader, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { AlertTriangle, AlertCircle, Info } from 'lucide-react';
import type { OverviewWarningDto, WarningSeverity } from '@/shared/types/system';

function severityColor(s: WarningSeverity): string {
  switch (s) {
    case 'Critical': return 'bg-red-100 text-red-800 border-red-200';
    case 'High':     return 'bg-orange-100 text-orange-800 border-orange-200';
    case 'Medium':   return 'bg-yellow-100 text-yellow-800 border-yellow-200';
    case 'Low':      return 'bg-blue-100 text-blue-800 border-blue-200';
  }
}

function severityBorderColor(s: WarningSeverity): string {
  switch (s) {
    case 'Critical': return '#ef4444';
    case 'High':     return '#f97316';
    case 'Medium':   return '#eab308';
    case 'Low':      return '#3b82f6';
  }
}

function SeverityIcon({ severity }: { severity: WarningSeverity }) {
  switch (severity) {
    case 'Critical':
    case 'High':   return <AlertCircle className="h-4 w-4" />;
    case 'Medium': return <AlertTriangle className="h-4 w-4" />;
    case 'Low':    return <Info className="h-4 w-4" />;
  }
}

const SEVERITY_ORDER: Record<WarningSeverity, number> = {
  Critical: 0,
  High: 1,
  Medium: 2,
  Low: 3,
};

interface Props {
  warnings: OverviewWarningDto[];
}

export function OverviewActionCards({ warnings }: Props) {
  const navigate = useNavigate();
  const sorted = [...warnings].sort(
    (a, b) => SEVERITY_ORDER[a.severity] - SEVERITY_ORDER[b.severity],
  );

  if (sorted.length === 0) {
    return (
      <div className="rounded-lg border border-dashed px-4 py-6 text-center text-sm text-muted-foreground">
        No actionable warnings — system is operating normally.
      </div>
    );
  }

  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {sorted.map((w, i) => (
        <Card
          key={`${w.type}-${i}`}
          className="border-l-4"
          style={{ borderLeftColor: severityBorderColor(w.severity) }}
        >
          <CardHeader className="pb-1 pt-3 px-4">
            <div className="flex items-center justify-between">
              <Badge className={`border text-xs ${severityColor(w.severity)}`}>
                <span className="mr-1 inline-flex">
                  <SeverityIcon severity={w.severity} />
                </span>
                {w.severity}
              </Badge>
            </div>
            <p className="text-sm font-semibold leading-tight mt-1">{w.title}</p>
          </CardHeader>
          <CardContent className="px-4 pb-3">
            <p className="text-xs text-muted-foreground mb-3">{w.description}</p>
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => navigate(w.targetRoute)}
            >
              Open →
            </Button>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
