import { useHasPermission } from '../../../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../../../shared/types/permissions';
import { useNodeManagement } from '../../NodeManagementProvider';
import { useRegistrationDetail } from '../../hooks/useRegistrationDetail';
import { useApproveRegistration } from '../../hooks/useApproveRegistration';
import { useRejectRegistration } from '../../hooks/useRejectRegistration';
import { DiffTable } from './DiffTable';
import { Button } from '../../../../components/ui/button';
import { CheckCheck, XCircle } from 'lucide-react';
import { toast } from 'sonner';

export function RegistrationDetailPanel() {
  const { selectedRegistration, setSelectedRegistration } = useNodeManagement();
  const canApprove = useHasPermission(PermissionKeys.ApproveNodes);
  const { data: detail, isLoading } = useRegistrationDetail(selectedRegistration?.id ?? null);
  const approve = useApproveRegistration();
  const reject  = useRejectRegistration();

  if (!selectedRegistration) {
    return (
      <div className="flex items-center justify-center h-full text-sm text-neutral-400">
        Select a registration to view details.
      </div>
    );
  }

  async function handleApprove() {
    await approve.mutateAsync({ id: selectedRegistration!.id });
    toast.success('Registration approved');
    setSelectedRegistration(null);
  }

  async function handleReject() {
    await reject.mutateAsync({ id: selectedRegistration!.id });
    toast.success('Registration rejected');
    setSelectedRegistration(null);
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
    </div>
  );
}
