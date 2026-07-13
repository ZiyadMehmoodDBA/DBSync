import { useQuery } from '@tanstack/react-query';
import { fetchSystemInfo, systemKeys } from '@/shared/api/system';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';

function fmtDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

export function LicensePage() {
  const { data, isLoading } = useQuery({
    queryKey: systemKeys.info,
    queryFn: fetchSystemInfo,
    staleTime: 60_000,
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">License &amp; System Info</h1>
        <p className="text-sm text-muted-foreground">
          Application version, runtime details, and edition information.
        </p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : !data ? (
        <p className="text-sm text-destructive">Failed to load system info.</p>
      ) : (
        <Card className="max-w-2xl">
          <CardHeader className="pb-2 pt-4 px-6">
            <div className="flex items-center gap-3">
              <div>
                <p className="text-2xl font-bold">MSOSync CE</p>
                <p className="text-sm text-muted-foreground">Community Edition</p>
              </div>
              <div className="ml-auto flex gap-2">
                <Badge className="bg-blue-100 text-blue-800 border-blue-200">
                  {data.edition}
                </Badge>
                <Badge
                  className={
                    data.environment === 'Production'
                      ? 'bg-red-100 text-red-800 border-red-200'
                      : 'bg-gray-100 text-gray-700 border-gray-200'
                  }
                >
                  {data.environment}
                </Badge>
              </div>
            </div>
          </CardHeader>
          <CardContent className="px-6 pb-6">
            <dl className="grid grid-cols-2 gap-x-8 gap-y-3 text-sm">
              <div>
                <dt className="text-muted-foreground text-xs">App Version</dt>
                <dd className="font-mono font-medium">{data.version}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Build Date</dt>
                <dd>{fmtDate(data.buildDate)}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Git Commit</dt>
                <dd className="font-mono text-xs truncate" title={data.gitCommit ?? undefined}>
                  {data.gitCommit ? data.gitCommit.slice(0, 12) : '—'}
                </dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">.NET Runtime</dt>
                <dd>{data.dotNetRuntime}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">OS</dt>
                <dd>{data.operatingSystem}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">DB Migration</dt>
                <dd className="font-mono text-xs">{data.databaseMigration ?? '—'}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Server Time</dt>
                <dd>{fmtDate(data.serverTime)}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Process Uptime</dt>
                <dd>{data.processUptime}</dd>
              </div>
            </dl>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
