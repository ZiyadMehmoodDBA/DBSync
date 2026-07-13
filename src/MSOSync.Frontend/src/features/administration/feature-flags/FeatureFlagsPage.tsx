import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getParametersByCategory, updateParameterByName } from '@/shared/api/parameters';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { Switch } from '@/components/ui/switch';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import type { ParameterMetadataDto } from '@/shared/types/parameters';

const FLAG_QUERY_KEY = ['parameters', 'FeatureFlag'] as const;

export function FeatureFlagsPage() {
  const qc = useQueryClient();
  const [search, setSearch] = useState('');

  const { data = [], isLoading } = useQuery<ParameterMetadataDto[]>({
    queryKey: FLAG_QUERY_KEY,
    queryFn: () => getParametersByCategory('FeatureFlag'),
    staleTime: 30_000,
  });

  const mutation = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameterByName(name, value),
    onSuccess: () => qc.invalidateQueries({ queryKey: FLAG_QUERY_KEY }),
  });

  const filtered = data.filter((p) =>
    (p.displayName ?? p.parameterName).toLowerCase().includes(search.toLowerCase()) ||
    p.parameterName.toLowerCase().includes(search.toLowerCase())
  );

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Feature Flags</h1>
        <p className="text-sm text-muted-foreground">
          Toggle experimental features. Changes take effect immediately unless marked &quot;Restart Required&quot;.
        </p>
      </div>

      <div className="flex items-center gap-3">
        <Input
          placeholder="Search flags…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="max-w-xs"
        />
        <span className="text-xs text-muted-foreground">{filtered.length} flags</span>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {filtered.map((p) => {
            const isEnabled = p.parameterValue === 'true' || p.parameterValue === '1';
            const isPending =
              mutation.isPending && mutation.variables?.name === p.parameterName;

            return (
              <Card key={p.parameterName}>
                <CardHeader className="pb-1 pt-4 px-4">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-sm font-semibold leading-tight truncate">
                        {p.displayName ?? p.parameterName}
                      </p>
                      <p className="text-xs font-mono text-muted-foreground truncate">{p.parameterName}</p>
                    </div>
                    <Switch
                      checked={isEnabled}
                      disabled={isPending}
                      onCheckedChange={(checked) =>
                        mutation.mutate({ name: p.parameterName, value: checked ? 'true' : 'false' })
                      }
                    />
                  </div>
                </CardHeader>
                <CardContent className="px-4 pb-4">
                  {p.description && (
                    <p className="text-xs text-muted-foreground mb-2">{p.description}</p>
                  )}
                  <div className="flex items-center gap-2">
                    {p.requiresRestart ? (
                      <Badge variant="outline" className="text-xs text-yellow-700 border-yellow-300 bg-yellow-50">
                        Restart Required
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-xs text-green-700 border-green-300 bg-green-50">
                        Live
                      </Badge>
                    )}
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
