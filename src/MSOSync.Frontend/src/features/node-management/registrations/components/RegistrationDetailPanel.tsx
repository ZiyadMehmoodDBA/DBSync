import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useHasPermission } from '../../../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../../../shared/types/permissions';
import { useNodeManagement } from '../../NodeManagementProvider';
import { useRegistrationDetail } from '../../hooks/useRegistrationDetail';
import { useApproveRegistration } from '../../hooks/useApproveRegistration';
import { useRejectRegistration } from '../../hooks/useRejectRegistration';
import { nodeManagementKeys } from '../../hooks/queryKeys';
import { DiffTable } from './DiffTable';
import { Button } from '../../../../components/ui/button';
import { CheckCheck, XCircle, Eye, EyeOff, Copy } from 'lucide-react';
import { toast } from 'sonner';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle,
} from '../../../../components/ui/dialog';
import { useNodeState } from '../../../../shared/hooks/useNodeLifecycle';
import { NodeStatusSummary } from '../../../../shared/components/node';
import { getAllNodes } from '../../../../shared/api/nodes';
import { useQuery } from '@tanstack/react-query';

/** Fetches live node state for the node currently behind the ExternalId */
function CurrentNodeContext({ nodeExternalId }: { nodeExternalId: string }) {
  // Look up node id from the all-nodes list (already likely cached)
  const { data: allNodes } = useQuery({
    queryKey: ['nodes'],
    queryFn: ({ signal }) => getAllNodes({ signal }),
    staleTime: 30_000,
  });
  // NodeDto.nodeId IS the external id in this system
  const node = allNodes?.find(n => n.nodeId === nodeExternalId);
  const { data: state } = useNodeState(node?.nodeId ?? nodeExternalId);
  if (!state) return null;
  return (
    <div className="mt-2">
      <p className="text-xs text-neutral-500 mb-1">Current node state:</p>
      <NodeStatusSummary
        lifecycle={state.lifecycleState}
        connectivity={state.connectivityStatus}
        connectivityReason={state.connectivityReason}
        maintenance={state.maintenanceMode}
        maintenanceReason={state.maintenanceReason}
      />
    </div>
  );
}

/** One-time token dialog shown after recovery approval */
function BootstrapTokenDialog({
  nodeId, token, open, onClose,
}: { nodeId: string; token: string; open: boolean; onClose: () => void }) {
  const [revealed, setRevealed] = useState(false);

  async function handleCopy() {
    await navigator.clipboard.writeText(token);
    toast.success('Token copied to clipboard');
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) onClose(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Recovery Bootstrap Token — {nodeId}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="rounded-lg border border-amber-300 bg-amber-50 dark:bg-amber-950/30 p-3">
            <p className="font-medium text-amber-700 dark:text-amber-300 text-sm">
              One-time token — save it now
            </p>
            <p className="text-xs text-amber-600 dark:text-amber-400 mt-1">
              This token will not be shown again. The node must use it to re-activate.
            </p>
          </div>
          <div className="space-y-2">
            <label className="text-sm font-medium">Bootstrap Token</label>
            <div className="flex items-center gap-2">
              <div className="flex-1 rounded-md border px-3 py-2 text-sm font-mono bg-neutral-50 dark:bg-neutral-900 dark:border-neutral-700 truncate">
                {revealed ? token : '•'.repeat(Math.min(token.length, 32))}
              </div>
              <Button
                size="icon"
                variant="ghost"
                onClick={() => setRevealed(r => !r)}
                title={revealed ? 'Hide token' : 'Reveal token'}
              >
                {revealed ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </Button>
              <Button size="icon" variant="ghost" onClick={() => { void handleCopy(); }} title="Copy token">
                <Copy className="h-4 w-4" />
              </Button>
            </div>
          </div>
          <Button className="w-full" onClick={onClose}>Done</Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export function RegistrationDetailPanel() {
  const qc = useQueryClient();
  const { selectedRegistration, setSelectedRegistration } = useNodeManagement();
  const canApprove = useHasPermission(PermissionKeys.ApproveNodes);
  const { data: detail, isLoading } = useRegistrationDetail(selectedRegistration?.id ?? null);
  const approve = useApproveRegistration();
  const reject  = useRejectRegistration();
  const [bootstrapToken, setBootstrapToken] = useState<{ nodeId: string; token: string } | null>(null);

  if (!selectedRegistration) {
    return (
      <div className="flex items-center justify-center h-full text-sm text-neutral-400">
        Select a registration to view details.
      </div>
    );
  }

  async function handleApprove() {
    try {
      const result = await approve.mutateAsync({ id: selectedRegistration!.id });
      await qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      await qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
      toast.success('Registration approved');
      setSelectedRegistration(null);
      // Show bootstrap token dialog if returned (recovery approvals)
      if (result.bootstrapToken) {
        setBootstrapToken({
          nodeId: selectedRegistration!.nodeExternalId,
          token: result.bootstrapToken,
        });
      }
    } catch {
      toast.error('Failed to approve registration');
    }
  }

  async function handleReject() {
    try {
      await reject.mutateAsync({ id: selectedRegistration!.id });
      await qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      await qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
      toast.success('Registration rejected');
      setSelectedRegistration(null);
    } catch {
      toast.error('Failed to reject registration');
    }
  }

  return (
    <div className="flex flex-col h-full overflow-y-auto p-4 gap-4">
      <div>
        <h3 className="font-semibold text-base">{selectedRegistration.nodeName}</h3>
        <p className="text-sm text-neutral-500">{selectedRegistration.nodeExternalId}</p>
        <div className="flex gap-2 mt-1 text-xs text-neutral-500">
          <span>{selectedRegistration.registrationType}</span>
          <span>·</span>
          <span>{selectedRegistration.status}</span>
        </div>
      </div>

      {canApprove && selectedRegistration.status === 'Pending' && (
        <div className="flex gap-2">
          <Button
            size="sm"
            onClick={() => { void handleApprove(); }}
            disabled={approve.isPending}
          >
            <CheckCheck className="h-4 w-4 mr-1" />
            Approve
          </Button>
          <Button
            size="sm"
            variant="destructive"
            onClick={() => { void handleReject(); }}
            disabled={reject.isPending}
          >
            <XCircle className="h-4 w-4 mr-1" />
            Reject
          </Button>
        </div>
      )}

      {isLoading && (
        <p className="text-sm text-neutral-400">Loading details…</p>
      )}

      {/* Recovery context panel — shown ABOVE the DiffViewer (spec §11.5) */}
      {detail?.registrationType === 'Recovery' && (
        <div className="mb-3 rounded border border-orange-300 bg-orange-50 p-3 text-sm dark:border-orange-800 dark:bg-orange-950/30">
          <p className="font-medium">Identity recovery request</p>
          <p className="mt-1">
            A node with a known External ID re-registered. Approving revokes ALL existing
            credentials for this node and issues a new one-time bootstrap token; the node
            re-activates before returning to service. Rejecting returns the node to its
            previous lifecycle state.
          </p>
          <CurrentNodeContext nodeExternalId={detail.nodeExternalId} />
        </div>
      )}

      {detail?.diff && <DiffTable diff={detail.diff} />}

      {detail?.metadata && (
        <div className="text-xs text-neutral-600 dark:text-neutral-400">
          <p className="font-medium mb-1">Metadata</p>
          {detail.metadata.machine?.hostName && (
            <p>Host: {detail.metadata.machine.hostName}</p>
          )}
          {detail.metadata.application?.agentVersion && (
            <p>Agent: {detail.metadata.application.agentVersion}</p>
          )}
        </div>
      )}

      {/* Bootstrap token one-time dialog */}
      {bootstrapToken && (
        <BootstrapTokenDialog
          nodeId={bootstrapToken.nodeId}
          token={bootstrapToken.token}
          open
          onClose={() => setBootstrapToken(null)}
        />
      )}
    </div>
  );
}
