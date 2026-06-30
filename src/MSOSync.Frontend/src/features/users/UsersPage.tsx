import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { UsersGrid } from './UsersGrid';
import { UserDialog } from './UserDialog';
import { useDeactivateUserMutation } from './mutations';
import { useAuth } from '../auth/useAuth';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { UserSummaryDto } from '../../shared/types';

export function UsersPage() {
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<UserSummaryDto | null>(null);
  const [deactivateState, setDeactivateState] = useState<UserSummaryDto | null>(null);

  const deactivateMutation = useDeactivateUserMutation();
  const { user } = useAuth();

  const onEdit = useCallback((row: UserSummaryDto) => { setEditState(row); }, []);
  const onDeactivate = useCallback((row: UserSummaryDto) => { setDeactivateState(row); }, []);

  const handleDeactivateConfirm = async () => {
    if (!deactivateState) return;
    try {
      await deactivateMutation.mutateAsync(deactivateState.userId);
      toast.success(`User "${deactivateState.username}" deactivated`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeactivateState(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Users</h1>
        <Button onClick={() => setCreateOpen(true)}>Add User</Button>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search users…"
        className="max-w-xs"
      />
      <UsersGrid
        quickFilterText={search}
        onEdit={onEdit}
        onDeactivate={onDeactivate}
        currentUsername={user?.username}
      />
      <UserDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <UserDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {deactivateState && (
        <ConfirmDialog
          open
          title="Deactivate User"
          description={`Deactivate user "${deactivateState.username}"? They will no longer be able to log in.`}
          confirmLabel="Deactivate"
          variant="destructive"
          loading={deactivateMutation.isPending}
          onConfirm={() => void handleDeactivateConfirm()}
          onOpenChange={(open) => { if (!open) setDeactivateState(null); }}
        />
      )}
    </div>
  );
}
