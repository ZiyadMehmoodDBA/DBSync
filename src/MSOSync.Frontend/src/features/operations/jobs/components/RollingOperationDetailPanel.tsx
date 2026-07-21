import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ConfirmDialog } from '@/shared/components/actions/ConfirmDialog';
import { formatRelativeTime } from '@/shared/utils/date';
import { useHasPermission } from '@/shared/hooks/usePermissions';
import { PermissionKeys } from '@/shared/types/permissions';
import {
  useRollingOperation, usePauseRollingOperation, useResumeRollingOperation,
  useAbortRollingOperation, useConfirmRollingStep,
} from '@/shared/hooks/useRollingOperations';
import { OperationStatusBadge } from './OperationStatusBadge';
import type { OperationStatus } from '@/shared/types/operations';
import type { RollingStepStatus } from '@/shared/types/rolling';
import { useState } from 'react';

const STEP_STATUS_CLASS: Record<RollingStepStatus, string> = {
  Pending:              'bg-gray-100 text-gray-600',
  Draining:             'bg-cyan-100 text-cyan-800',
  InMaintenance:        'bg-amber-100 text-amber-800',
  AwaitingVerification: 'bg-blue-100 text-blue-800',
  Completed:            'bg-green-100 text-green-800',
  Failed:               'bg-red-100 text-red-800',
  Skipped:              'bg-neutral-100 text-neutral-500',
};

interface Props {
  operationId: string;
  onClose: () => void;
}

export function RollingOperationDetailPanel({ operationId, onClose }: Props) {
  const [abortDialogOpen, setAbortDialogOpen] = useState(false);
  const canManage = useHasPermission(PermissionKeys.ManageNodeLifecycle);
  const { data, isLoading } = useRollingOperation(operationId);
  const pauseMutation  = usePauseRollingOperation();
  const resumeMutation = useResumeRollingOperation();
  const abortMutation  = useAbortRollingOperation();
  const confirmStep    = useConfirmRollingStep();

  if (isLoading || !data) {
    return (
      <div className="fixed inset-y-0 right-0 w-96 border-l bg-background p-4 overflow-y-auto z-40">
        <div className="flex justify-between items-center mb-4">
          <span className="font-medium">Rolling Operation</span>
          <button type="button" onClick={onClose} className="text-xs text-neutral-400 hover:text-neutral-600">Close</button>
        </div>
        <span className="text-muted-foreground text-sm">{isLoading ? 'Loading…' : 'Not found'}</span>
      </div>
    );
  }

  const { policy, steps, status, result, operationType } = data;
  const waveNumbers = [...new Set(steps.map(s => s.waveNumber))].sort((a, b) => a - b);

  return (
    <div className="fixed inset-y-0 right-0 w-96 border-l bg-background p-4 overflow-y-auto z-40 shadow-lg">
      <div className="flex justify-between items-center mb-4">
        <div className="flex items-center gap-2">
          <Badge className="text-xs">{operationType}</Badge>
          <OperationStatusBadge status={status as OperationStatus} result={result as never} />
        </div>
        <button type="button" onClick={onClose} className="text-xs text-neutral-400 hover:text-neutral-600">Close</button>
      </div>

      {result && <p className="text-xs text-muted-foreground mb-3">Result: {result}</p>}

      {/* Policy summary */}
      <div className="text-xs text-muted-foreground mb-4 space-y-0.5">
        <p>Wave: {policy.waveSize != null ? `${policy.waveSize} nodes` : `${policy.wavePercent ?? '?'}%`}</p>
        <p>Action: {policy.waveAction}{policy.waveAction === 'auto-window' ? ` (${policy.windowSeconds}s)` : ''}</p>
        <p>Gate soak: {policy.gateSoakSeconds}s · Verification timeout: {policy.verificationTimeoutSeconds}s</p>
        {policy.targetVersion && <p>Target: {policy.targetVersion}</p>}
      </div>

      {/* Steps grouped by wave */}
      <div className="space-y-4">
        {waveNumbers.map(wave => (
          <div key={wave}>
            <p className="text-xs font-semibold mb-1">Wave {wave}</p>
            <div className="space-y-1">
              {steps.filter(s => s.waveNumber === wave).map(step => (
                <div key={step.stepId} className="rounded border p-2 text-xs space-y-1">
                  <div className="flex items-center justify-between">
                    <span className="font-medium">{step.nodeId}</span>
                    <Badge className={`text-xs ${STEP_STATUS_CLASS[step.status] ?? ''}`}>
                      {step.status}
                    </Badge>
                  </div>
                  {step.startedAt && (
                    <p className="text-muted-foreground">Started {formatRelativeTime(step.startedAt)}</p>
                  )}
                  {step.completedAt && (
                    <p className="text-muted-foreground">Completed {formatRelativeTime(step.completedAt)}</p>
                  )}
                  {step.errorMessage && (
                    <p className="text-red-600">{step.errorMessage}</p>
                  )}
                  {status === 'Running' && step.status === 'InMaintenance' && policy.waveAction === 'manual-confirm' && canManage && (
                    <Button
                      size="sm" variant="outline" className="h-6 text-xs"
                      disabled={confirmStep.isPending}
                      onClick={() => confirmStep.mutate(step.stepId)}
                    >
                      Confirm
                    </Button>
                  )}
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>

      {/* Operation actions */}
      {canManage && (status === 'Running' || status === 'Paused') && (
        <div className="mt-4 flex gap-2">
          {status === 'Running' && (
            <Button size="sm" variant="outline" disabled={pauseMutation.isPending}
              onClick={() => pauseMutation.mutate(operationId)}>
              Pause
            </Button>
          )}
          {status === 'Paused' && (
            <Button size="sm" variant="outline" disabled={resumeMutation.isPending}
              onClick={() => resumeMutation.mutate(operationId)}>
              Resume
            </Button>
          )}
          <Button size="sm" variant="destructive" onClick={() => setAbortDialogOpen(true)}>
            Abort
          </Button>
        </div>
      )}

      <ConfirmDialog
        open={abortDialogOpen}
        title="Abort rolling operation?"
        description="Aborts remaining steps and restores in-flight nodes to Active."
        confirmLabel={abortMutation.isPending ? 'Aborting…' : 'Abort'}
        variant="destructive"
        loading={abortMutation.isPending}
        onConfirm={() => {
          abortMutation.mutate(operationId, { onSuccess: () => setAbortDialogOpen(false) });
        }}
        onOpenChange={(o) => { if (!o) setAbortDialogOpen(false); }}
      />
    </div>
  );
}
