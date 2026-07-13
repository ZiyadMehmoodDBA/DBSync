import { useQuery } from '@tanstack/react-query';
import { fetchSystemInfo, systemKeys } from '@/shared/api/system';
import { Badge } from '@/components/ui/badge';

export function OverviewSystemInfo() {
  const { data } = useQuery({
    queryKey: systemKeys.info,
    queryFn: fetchSystemInfo,
    staleTime: 60_000,
  });

  if (!data) return null;

  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-1 rounded-lg border bg-muted/40 px-4 py-2 text-xs text-muted-foreground">
      <span>
        <span className="font-medium text-foreground">v{data.version}</span>
      </span>
      <span>DB migration: {data.databaseMigration}</span>
      <span>
        Env:{' '}
        <Badge variant="outline" className="h-4 px-1 text-xs">
          {data.environment}
        </Badge>
      </span>
      <span>Uptime: {data.processUptime}</span>
      <span>.NET {data.dotNetRuntime}</span>
      <span>{data.operatingSystem}</span>
    </div>
  );
}
