import { useState, useCallback } from 'react';
import { ParametersGrid } from './ParametersGrid';
import { ParameterDialog } from './ParameterDialog';
import type { ParameterRow } from './columns';

export function ParametersPage() {
  const [editState, setEditState] = useState<ParameterRow | null>(null);

  const onEdit = useCallback((row: ParameterRow) => {
    setEditState(row);
  }, []);

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Parameters</h1>
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
