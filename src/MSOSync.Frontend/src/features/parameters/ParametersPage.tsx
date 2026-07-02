import { useState, useCallback } from 'react';
import { ParametersGrid } from './ParametersGrid';
import { ParameterDialog } from './ParameterDialog';
import { useParameters } from './hooks';
import { ExportMenu } from '../../shared/components/ExportMenu';
import type { ParameterRow } from './columns';

export function ParametersPage() {
  const [editState, setEditState] = useState<ParameterRow | null>(null);
  const { data: paramsData } = useParameters();

  const onEdit = useCallback((row: ParameterRow) => {
    setEditState(row);
  }, []);

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Parameters</h1>
        <ExportMenu
          resource="parameters"
          currentData={(paramsData ?? []) as unknown as Record<string, unknown>[]}
          queryParams={{}}
          supportsAllRows={false}
        />
      </div>
      <ParametersGrid onEdit={onEdit} />
      {editState && (
        <ParameterDialog
          open={!!editState}
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
    </div>
  );
}
