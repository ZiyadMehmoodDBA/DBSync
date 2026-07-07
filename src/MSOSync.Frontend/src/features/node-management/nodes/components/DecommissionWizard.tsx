import { useState } from 'react';
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle,
} from '../../../../components/ui/dialog';
import { Button } from '../../../../components/ui/button';
import { useDecommissionNode, useNodeState } from '../../../../shared/hooks/useNodeLifecycle';

const REASON_PRESETS = [
  'Hardware Replacement', 'Site Closure', 'Migration',
  'Duplicate Node', 'Security Incident', 'Manual',
] as const;

/** 3-step wizard (12A provision-wizard pattern, spec §11.3):
 *  1. reason preset + free text + grace period
 *  2. impact preview (drain snapshot + credential revocation warning)
 *  3. typed confirmation ("decommission")
 */
export function DecommissionWizard({
  nodeId, nodeName, open, onOpenChange,
}: { nodeId: string; nodeName: string; open: boolean; onOpenChange: (open: boolean) => void }) {
  const [step, setStep] = useState(1);
  const [preset, setPreset] = useState<string>('');
  const [reasonText, setReasonText] = useState('');
  const [graceMinutes, setGraceMinutes] = useState<number | ''>('');
  const [confirmText, setConfirmText] = useState('');
  const mutation = useDecommissionNode();
  const { data: state } = useNodeState(nodeId);   // heartbeat/connectivity context for impact step

  const reason = [preset, reasonText.trim()].filter(Boolean).join(': ');

  const submit = () => {
    mutation.mutate(
      { nodeId, reason, gracePeriodMinutes: graceMinutes === '' ? undefined : graceMinutes },
      { onSuccess: () => { onOpenChange(false); setStep(1); setConfirmText(''); } },
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Decommission {nodeName} — step {step} of 3</DialogTitle>
        </DialogHeader>

        {step === 1 && (
          <div className="space-y-3">
            <div className="text-sm font-medium">Reason</div>
            <div className="flex flex-wrap gap-2">
              {REASON_PRESETS.map((p) => (
                <Button
                  key={p}
                  size="sm"
                  variant={preset === p ? 'default' : 'outline'}
                  onClick={() => setPreset(p)}
                >
                  {p}
                </Button>
              ))}
            </div>
            <textarea
              className="w-full rounded border p-2 text-sm"
              placeholder="Details (required)"
              value={reasonText}
              onChange={(e) => setReasonText(e.target.value)}
              rows={2}
            />
            <label className="block text-sm">
              Grace period minutes (default 60)
              <input
                type="number"
                min={1}
                className="mt-1 w-full rounded border p-2 text-sm"
                value={graceMinutes}
                onChange={(e) => setGraceMinutes(e.target.value === '' ? '' : Number(e.target.value))}
              />
            </label>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3 text-sm">
            <p className="font-medium">Impact preview</p>
            <ul className="list-disc space-y-1 pl-5">
              <li>New sync work freezes immediately; in-flight batches drain until complete or grace expiry.</li>
              <li>
                Last heartbeat: {state?.lastHeartbeatUtc ?? 'never'} · Connectivity:{' '}
                {state?.connectivityStatus ?? 'Unknown'}
              </li>
              <li className="font-medium text-red-600">
                All node credentials (bootstrap + auth tokens) are revoked at start. This node can
                never rejoin under this identity — a returning machine must register as a new node.
              </li>
              <li>The node record is preserved permanently and hidden from default views.</li>
            </ul>
          </div>
        )}

        {step === 3 && (
          <div className="space-y-3 text-sm">
            <p>
              Type <span className="font-mono font-bold">decommission</span> to confirm.
            </p>
            <input
              className="w-full rounded border p-2 text-sm"
              value={confirmText}
              onChange={(e) => setConfirmText(e.target.value)}
              autoFocus
            />
          </div>
        )}

        <DialogFooter>
          {step > 1 && <Button variant="outline" onClick={() => setStep(step - 1)}>Back</Button>}
          {step < 3 && (
            <Button onClick={() => setStep(step + 1)} disabled={step === 1 && !reasonText.trim()}>
              Next
            </Button>
          )}
          {step === 3 && (
            <Button
              variant="destructive"
              disabled={confirmText !== 'decommission' || mutation.isPending}
              onClick={submit}
            >
              {mutation.isPending ? 'Starting…' : 'Decommission Node'}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
