import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { ChannelsGrid } from './ChannelsGrid';
import { ChannelDialog } from './ChannelDialog';
import { useDeleteChannelMutation } from './mutations';
import { useChannels } from './hooks';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { ChannelDto } from '../../shared/types';

export function ChannelsPage() {
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<ChannelDto | null>(null);
  const [deleteState, setDeleteState] = useState<ChannelDto | null>(null);

  const deleteMutation = useDeleteChannelMutation();
  const { data: channelsData } = useChannels();

  const onEdit = useCallback((row: ChannelDto) => { setEditState(row); }, []);
  const onDelete = useCallback((row: ChannelDto) => { setDeleteState(row); }, []);

  const handleDeleteConfirm = async () => {
    if (!deleteState) return;
    try {
      await deleteMutation.mutateAsync(deleteState.channelId);
      toast.success(`Channel "${deleteState.channelId}" deleted`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeleteState(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Channels</h1>
        <div className="flex items-center gap-2">
          <ExportMenu
            resource="channels"
            currentData={(channelsData ?? []) as unknown as Record<string, unknown>[]}
            queryParams={{}}
            supportsAllRows={false}
          />
          <Button onClick={() => setCreateOpen(true)}>Add Channel</Button>
        </div>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search channels…"
        className="max-w-xs"
      />
      <ChannelsGrid quickFilterText={search} onEdit={onEdit} onDelete={onDelete} />
      <ChannelDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <ChannelDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {deleteState && (
        <ConfirmDialog
          open
          title="Delete Channel"
          description={`Delete channel "${deleteState.channelId}"? This cannot be undone.`}
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
