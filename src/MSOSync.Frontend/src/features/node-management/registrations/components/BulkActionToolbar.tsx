import { useQueryClient } from '@tanstack/react-query';
import { Button } from '../../../../components/ui/button';
import { CheckCheck, XCircle, X } from 'lucide-react';
import { useNodeManagement } from '../../NodeManagementProvider';
import { useBulkApproveRegistrations } from '../../hooks/useBulkApproveRegistrations';
import { useBulkRejectRegistrations } from '../../hooks/useBulkRejectRegistrations';
import { nodeManagementKeys } from '../../hooks/queryKeys';
import { toast } from 'sonner';

export function BulkActionToolbar() {
  const qc = useQueryClient();
  const { bulkSelection, clearBulkSelection } = useNodeManagement();
  const bulkApprove = useBulkApproveRegistrations();
  const bulkReject  = useBulkRejectRegistrations();

  const count = bulkSelection.size;
  if (count === 0) return null;

  async function handleBulkApprove() {
    try {
      await bulkApprove.mutateAsync({ ids: Array.from(bulkSelection) });
      await qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      await qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
      clearBulkSelection();
      toast.success(`Approved ${count} registration${count !== 1 ? 's' : ''}`);
    } catch {
      toast.error('Failed to approve registrations');
    }
  }

  async function handleBulkReject() {
    try {
      await bulkReject.mutateAsync({ ids: Array.from(bulkSelection) });
      await qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      await qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
      clearBulkSelection();
      toast.success(`Rejected ${count} registration${count !== 1 ? 's' : ''}`);
    } catch {
      toast.error('Failed to reject registrations');
    }
  }

  return (
    <div className="sticky top-0 z-10 flex items-center gap-2 bg-blue-50 dark:bg-blue-950/30 border-b border-blue-200 dark:border-blue-800 px-4 py-2">
      <span className="text-sm font-medium text-blue-700 dark:text-blue-300">
        {count} selected
      </span>
      <Button
        size="sm"
        variant="default"
        onClick={() => { void handleBulkApprove(); }}
        disabled={bulkApprove.isPending}
      >
        <CheckCheck className="h-4 w-4 mr-1" />
        Approve {count}
      </Button>
      <Button
        size="sm"
        variant="destructive"
        onClick={() => { void handleBulkReject(); }}
        disabled={bulkReject.isPending}
      >
        <XCircle className="h-4 w-4 mr-1" />
        Reject {count}
      </Button>
      <Button size="sm" variant="ghost" onClick={clearBulkSelection}>
        <X className="h-4 w-4" />
      </Button>
    </div>
  );
}
