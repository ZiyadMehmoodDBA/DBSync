import { useQuery } from '@tanstack/react-query';
import { fetchSystemHealth, systemKeys } from '@/shared/api/system';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import type { HealthLevel } from '@/shared/types/system';

const LEVEL_COLORS: Record<HealthLevel, string> = {
  Healthy:   'bg-green-100 text-green-800 border-green-200',
  Degraded:  'bg-yellow-100 text-yellow-800 border-yellow-200',
  Unhealthy: 'bg-red-100 text-red-800 border-red-200',
  Critical:  'bg-red-100 text-red-800 border-red-200',
  Unknown:   'bg-gray-100 text-gray-600 border-gray-200',
};

export function SystemHealthPanel() {
  const { data, isLoading } = useQuery({
    queryKey: systemKeys.health,
    queryFn: fetchSystemHealth,
    staleTime: 15_000,
    refetchOnWindowFocus: true,
  });

  if (isLoading) {
    return <p className="text-xs text-muted-foreground">Loading health...</p>;
  }

  if (!data?.length) {
    return <p className="text-xs text-muted-foreground">No health data available.</p>;
  }

  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {data.map((c) => (
        <Card key={c.name}>
          <CardHeader className="pb-1 pt-3 px-4">
            <div className="flex items-center justify-between">
              <p className="text-sm font-medium">{c.name}</p>
              <Badge className={`border text-xs ${LEVEL_COLORS[c.level] ?? LEVEL_COLORS['Unknown']}`}>
                {c.level}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="px-4 pb-3">
            <p className="text-xs text-muted-foreground">{c.summary}</p>
            {c.detail && (
              <p className="text-xs text-muted-foreground mt-0.5">{c.detail}</p>
            )}
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
