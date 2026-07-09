import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getParametersByCategory, updateParameterByName } from '@/shared/api/parameters';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import type { ParameterMetadataDto } from '@/shared/types/parameters';

const EXCLUDED_CATEGORIES = ['FeatureFlag', 'Retention'];
const SETTINGS_QUERY_KEY = ['parameters', 'settings-all'] as const;

function groupByCategory(params: ParameterMetadataDto[]): Record<string, ParameterMetadataDto[]> {
  return params.reduce<Record<string, ParameterMetadataDto[]>>((acc, p) => {
    const cat = p.category ?? 'General';
    if (!acc[cat]) acc[cat] = [];
    acc[cat].push(p);
    return acc;
  }, {});
}

export function SettingsPage() {
  const qc = useQueryClient();
  const [edits, setEdits] = useState<Record<string, string>>({});

  const { data = [], isLoading } = useQuery<ParameterMetadataDto[]>({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: () => getParametersByCategory(),
    staleTime: 30_000,
    select: (all) =>
      all.filter((p) => !EXCLUDED_CATEGORIES.includes(p.category ?? '')),
  });

  const mutation = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameterByName(name, value),
    onSuccess: () => qc.invalidateQueries({ queryKey: SETTINGS_QUERY_KEY }),
  });

  const grouped = groupByCategory(data);

  return (
    <div className="space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Settings</h1>
        <p className="text-sm text-muted-foreground">System configuration parameters grouped by category.</p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        Object.entries(grouped).map(([category, params]) => (
          <section key={category}>
            <h2 className="mb-3 text-sm font-semibold uppercase tracking-wider text-muted-foreground">
              {category}
            </h2>
            <div className="space-y-2">
              {params.map((p) => {
                const currentEdit = edits[p.parameterName] ?? (p.parameterValue ?? '');
                const isDirty = currentEdit !== (p.parameterValue ?? '');
                const isPending =
                  mutation.isPending && mutation.variables?.name === p.parameterName;

                return (
                  <Card key={p.parameterName}>
                    <CardContent className="flex items-start gap-4 px-4 py-3">
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2 mb-1">
                          <p className="text-sm font-medium">{p.displayName ?? p.parameterName}</p>
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
                        {p.description && (
                          <p className="text-xs text-muted-foreground mb-1">{p.description}</p>
                        )}
                        {(p.minimumValue != null || p.maximumValue != null) && (
                          <p className="text-xs text-muted-foreground">
                            Range:{' '}
                            {p.minimumValue ?? '—'}
                            {' – '}
                            {p.maximumValue ?? '—'}
                          </p>
                        )}
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        <Input
                          type={p.valueType === 'Integer' || p.valueType === 'Number' ? 'number' : 'text'}
                          value={currentEdit}
                          className="w-32 h-8 text-sm"
                          min={p.minimumValue ?? undefined}
                          max={p.maximumValue ?? undefined}
                          onChange={(e) =>
                            setEdits((prev) => ({ ...prev, [p.parameterName]: e.target.value }))
                          }
                        />
                        <Button
                          size="sm"
                          className="h-8"
                          disabled={!isDirty || isPending}
                          onClick={() =>
                            mutation.mutate({ name: p.parameterName, value: currentEdit })
                          }
                        >
                          {isPending ? 'Saving…' : 'Save'}
                        </Button>
                      </div>
                    </CardContent>
                  </Card>
                );
              })}
            </div>
          </section>
        ))
      )}
    </div>
  );
}
