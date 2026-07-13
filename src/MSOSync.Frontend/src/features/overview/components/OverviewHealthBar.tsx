import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { RefreshCw } from 'lucide-react';
import type { OverviewHealthWidget, OverviewOperationsWidget, HealthLevel } from '@/shared/types/system';

function healthColor(level: HealthLevel): string {
  switch (level) {
    case 'Healthy':   return 'bg-green-100 text-green-800 border-green-200';
    case 'Degraded':  return 'bg-yellow-100 text-yellow-800 border-yellow-200';
    case 'Unhealthy': return 'bg-red-100 text-red-800 border-red-200';
    case 'Critical':  return 'bg-red-100 text-red-800 border-red-200';
    default:          return 'bg-gray-100 text-gray-600 border-gray-200';
  }
}

interface Props {
  health: OverviewHealthWidget;
  operations: OverviewOperationsWidget;
  lastRefreshedAt: string;
  onRefresh: () => void;
  isRefreshing: boolean;
}

export function OverviewHealthBar({ health, operations, lastRefreshedAt, onRefresh, isRefreshing }: Props) {
  const refreshedDate = new Date(lastRefreshedAt);

  return (
    <div className="flex flex-wrap items-center gap-4 rounded-lg border bg-card px-4 py-3">
      <Badge className={`border ${healthColor(health.clusterHealth)}`}>
        Cluster: {health.clusterHealth}
      </Badge>
      <Badge className={`border ${healthColor(health.workerHealth)}`}>
        Workers: {health.workerHealth}
      </Badge>
      <Badge className={`border ${healthColor(health.nodeHealth)}`}>
        Nodes: {health.nodeHealth}
      </Badge>

      <div className="text-sm text-muted-foreground">
        Active jobs: <span className="font-medium text-foreground">{operations.running}</span>
      </div>
      {operations.failedToday > 0 && (
        <div className="text-sm text-muted-foreground">
          Failed today: <span className="font-medium text-destructive">{operations.failedToday}</span>
        </div>
      )}

      <div className="ml-auto flex items-center gap-2 text-xs text-muted-foreground">
        <span>Last refreshed {refreshedDate.toLocaleTimeString()}</span>
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={onRefresh}
          disabled={isRefreshing}
          title="Refresh overview"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${isRefreshing ? 'animate-spin' : ''}`} />
        </Button>
      </div>
    </div>
  );
}
