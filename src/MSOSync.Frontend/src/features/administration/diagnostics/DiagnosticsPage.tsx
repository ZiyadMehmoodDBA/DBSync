import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { fetchSystemHealth, systemKeys } from '@/shared/api/system';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { ChevronRight, Database, Cpu, Activity, Server } from 'lucide-react';
import type { HealthLevel } from '@/shared/types/system';
import type { ReactNode } from 'react';

const LEVEL_COLORS: Record<HealthLevel, string> = {
  Healthy:  'bg-green-100 text-green-800 border-green-200',
  Degraded: 'bg-yellow-100 text-yellow-800 border-yellow-200',
  Critical: 'bg-red-100 text-red-800 border-red-200',
  Unknown:  'bg-gray-100 text-gray-600 border-gray-200',
};

const CONTRIBUTOR_ICON: Record<string, ReactNode> = {
  Database:  <Database className="h-5 w-5" />,
  Workers:   <Cpu className="h-5 w-5" />,
  Activity:  <Activity className="h-5 w-5" />,
  API:       <Server className="h-5 w-5" />,
};

const CONTRIBUTOR_NAVIGATE: Record<string, string | null> = {
  Database: '/operations/health',
  Workers:  '/operations/health',
  Activity: '/operations/activity',
  API:      null,
};

export function DiagnosticsPage() {
  const navigate = useNavigate();

  const { data = [], isLoading } = useQuery({
    queryKey: systemKeys.health,
    queryFn: fetchSystemHealth,
    staleTime: 15_000,
    refetchOnWindowFocus: true,
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Diagnostics</h1>
        <p className="text-sm text-muted-foreground">
          Health contributors — click a tile to drill into details.
        </p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {data.map((c) => {
            const navigateTo = CONTRIBUTOR_NAVIGATE[c.name] ?? null;
            const levelColor = LEVEL_COLORS[c.level] ?? LEVEL_COLORS['Unknown'];

            return (
              <Card
                key={c.name}
                className={navigateTo ? 'cursor-pointer hover:bg-muted/40 transition-colors' : ''}
                onClick={() => { if (navigateTo) void navigate(navigateTo); }}
              >
                <CardHeader className="pb-1 pt-4 px-4">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 text-muted-foreground">
                      {CONTRIBUTOR_ICON[c.name] ?? <Server className="h-5 w-5" />}
                      <p className="text-sm font-semibold text-foreground">{c.name}</p>
                    </div>
                    <div className="flex items-center gap-1">
                      <Badge className={`border text-xs ${levelColor}`}>
                        {c.level}
                      </Badge>
                      {navigateTo && <ChevronRight className="h-4 w-4 text-muted-foreground" />}
                    </div>
                  </div>
                </CardHeader>
                <CardContent className="px-4 pb-4">
                  {c.summary && (
                    <p className="text-xs text-muted-foreground">{c.summary}</p>
                  )}
                  {c.detail && (
                    <p className="text-xs text-muted-foreground mt-0.5">{c.detail}</p>
                  )}
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
