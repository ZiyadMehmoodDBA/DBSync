import { useState } from 'react';
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle,
} from '../../../../components/ui/dialog';
import { Button } from '../../../../components/ui/button';
import { useStartMaintenance } from '../../../../shared/hooks/useNodeLifecycle';

export function MaintenanceDialog({
  nodeId, open, onOpenChange,
}: { nodeId: string; open: boolean; onOpenChange: (open: boolean) => void }) {
  const [reason, setReason] = useState('');
  const [expectedEndAt, setExpectedEndAt] = useState('');
  const [notifyNode, setNotifyNode] = useState(false);
  const mutation = useStartMaintenance();

  const submit = () => {
    mutation.mutate(
      {
        nodeId,
        reason: reason.trim(),
        expectedEndAt: expectedEndAt ? new Date(expectedEndAt).toISOString() : undefined,
        notifyNode,
      },
      { onSuccess: () => onOpenChange(false) },
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>Start Maintenance — {nodeId}</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <label className="block text-sm">
            Reason <span className="text-red-500">*</span>
            <textarea
              className="mt-1 w-full rounded border p-2 text-sm"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={2}
            />
          </label>
          <label className="block text-sm">
            Expected end (optional)
            <input
              type="datetime-local"
              className="mt-1 w-full rounded border p-2 text-sm"
              value={expectedEndAt}
              onChange={(e) => setExpectedEndAt(e.target.value)}
            />
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={notifyNode} onChange={(e) => setNotifyNode(e.target.checked)} />
            Notify node (best effort)
          </label>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button onClick={submit} disabled={!reason.trim() || mutation.isPending}>
            {mutation.isPending ? 'Starting…' : 'Start Maintenance'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
