import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getParametersByCategory, updateParameterByName } from '@/shared/api/parameters';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { ParameterMetadataDto } from '@/shared/types/parameters';

const RETENTION_QUERY_KEY = ['parameters', 'Retention'] as const;

export function RetentionPage() {
  const qc = useQueryClient();
  const [edits, setEdits] = useState<Record<string, string>>({});

  const { data = [], isLoading } = useQuery<ParameterMetadataDto[]>({
    queryKey: RETENTION_QUERY_KEY,
    queryFn: () => getParametersByCategory('Retention'),
    staleTime: 60_000,
  });

  const mutation = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameterByName(name, value),
    onSuccess: () => qc.invalidateQueries({ queryKey: RETENTION_QUERY_KEY }),
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Retention Policies</h1>
        <p className="text-sm text-muted-foreground">
          Configure how long historical data is kept. Lower values reduce storage but limit audit history.
        </p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Policy</TableHead>
                <TableHead>Description</TableHead>
                <TableHead className="w-[200px]">Value</TableHead>
                <TableHead className="w-[80px]" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((p) => {
                const currentEdit = edits[p.parameterName] ?? (p.parameterValue ?? '');
                const isDirty = currentEdit !== (p.parameterValue ?? '');
                const isPending =
                  mutation.isPending && mutation.variables?.name === p.parameterName;

                return (
                  <TableRow key={p.parameterName}>
                    <TableCell>
                      <p className="text-sm font-medium">{p.displayName ?? p.parameterName}</p>
                      <p className="text-xs font-mono text-muted-foreground">{p.parameterName}</p>
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground max-w-[280px] whitespace-normal">
                      {p.description ?? '—'}
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        value={currentEdit}
                        min={p.minimumValue ?? undefined}
                        max={p.maximumValue ?? undefined}
                        className="h-8 text-sm"
                        onChange={(e) =>
                          setEdits((prev) => ({ ...prev, [p.parameterName]: e.target.value }))
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <Button
                        size="sm"
                        className="h-8"
                        disabled={!isDirty || isPending}
                        onClick={() =>
                          mutation.mutate({ name: p.parameterName, value: currentEdit })
                        }
                      >
                        {isPending ? '…' : 'Save'}
                      </Button>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
