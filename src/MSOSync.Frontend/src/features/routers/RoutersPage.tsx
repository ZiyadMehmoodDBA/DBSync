import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { RoutersGrid } from './RoutersGrid';
import { RouterDialog } from './RouterDialog';
import { useDeleteRouterMutation } from './mutations';
import { useRouters } from './hooks';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { RouterDto } from '../../shared/types';

export function RoutersPage() {
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<RouterDto | null>(null);
  const [deleteState, setDeleteState] = useState<RouterDto | null>(null);

  const deleteMutation = useDeleteRouterMutation();
  const { data: routersData } = useRouters();

  const onEdit = useCallback((row: RouterDto) => { setEditState(row); }, []);
  const onDelete = useCallback((row: RouterDto) => { setDeleteState(row); }, []);

  const handleDeleteConfirm = async () => {
    if (!deleteState) return;
    try {
      await deleteMutation.mutateAsync(deleteState.routerId);
      toast.success(`Router "${deleteState.routerId}" deleted`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeleteState(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Routers</h1>
        <div className="flex items-center gap-2">
          <ExportMenu
            resource="routers"
            currentData={(routersData ?? []) as unknown as Record<string, unknown>[]}
            queryParams={{}}
            supportsAllRows={false}
          />
          <Button onClick={() => setCreateOpen(true)}>Add Router</Button>
        </div>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search routers…"
        className="max-w-xs"
      />
      <RoutersGrid quickFilterText={search} onEdit={onEdit} onDelete={onDelete} />
      <RouterDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <RouterDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {deleteState && (
        <ConfirmDialog
          open
          title="Delete Router"
          description={`Delete router "${deleteState.routerId}"? This cannot be undone.`}
          confirmLabel="Delete"
          variant="destructive"
          loading={deleteMutation.isPending}
          onConfirm={() => void handleDeleteConfirm()}
          onOpenChange={(open) => { if (!open) setDeleteState(null); }}
        />
      )}
    </div>
  );
}
