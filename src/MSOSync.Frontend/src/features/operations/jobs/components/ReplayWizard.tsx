import { useState } from 'react';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useCreateReplay } from '@/shared/hooks/useReplayOperations';
import type { ReplayMode } from '@/shared/types/replay';

interface Props { open: boolean; onOpenChange: (open: boolean) => void }

type Step = 1 | 2 | 3 | 4;

export function ReplayWizard({ open, onOpenChange }: Props) {
  const [step, setStep]           = useState<Step>(1);
  const [mode, setMode]           = useState<ReplayMode>('FailedDelivery');
  const [nodeId, setNodeId]       = useState('');
  const [fromTime, setFromTime]   = useState('');
  const [toTime, setToTime]       = useState('');
  const [batchIdsText, setBatchIdsText] = useState('');
  const [rangeError, setRangeError]     = useState('');

  const createMutation = useCreateReplay();

  const validateRange = () => {
    if (!fromTime || !toTime) return false;
    const from = new Date(fromTime);
    const to   = new Date(toTime);
    if (from >= to) { setRangeError('From must be before To'); return false; }
    const days = (to.getTime() - from.getTime()) / 86400000;
    if (days > 90) { setRangeError('Range cannot exceed 90 days'); return false; }
    setRangeError('');
    return true;
  };

  const handleNext = () => {
    if (step === 3 && !validateRange()) return;
    setStep((s) => Math.min(s + 1, 4) as Step);
  };

  const handleSubmit = () => {
    const batchIds = mode === 'FailedDelivery' && batchIdsText.trim()
      ? batchIdsText.split(',').map((s) => parseInt(s.trim(), 10)).filter((n) => !isNaN(n))
      : undefined;

    createMutation.mutate(
      { nodeId, replayMode: mode, fromTime, toTime, batchIds: batchIds ?? null },
      { onSuccess: () => { onOpenChange(false); resetForm(); } },
    );
  };

  const resetForm = () => {
    setStep(1); setMode('FailedDelivery'); setNodeId('');
    setFromTime(''); setToTime(''); setBatchIdsText(''); setRangeError('');
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) resetForm(); onOpenChange(o); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>New Replay — Step {step} of 4</DialogTitle>
        </DialogHeader>

        {step === 1 && (
          <div className="space-y-3">
            <Label>Replay Mode</Label>
            <div className="space-y-2">
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="radio"
                  id="fd"
                  name="replayMode"
                  value="FailedDelivery"
                  checked={mode === 'FailedDelivery'}
                  onChange={() => setMode('FailedDelivery')}
                />
                <Label htmlFor="fd">Failed Delivery — re-queue batches stuck in Error</Label>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="radio"
                  id="md"
                  name="replayMode"
                  value="MissedData"
                  checked={mode === 'MissedData'}
                  onChange={() => setMode('MissedData')}
                />
                <Label htmlFor="md">Missed Data — re-create batches for events node missed</Label>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="radio"
                  id="both"
                  name="replayMode"
                  value="Both"
                  checked={mode === 'Both'}
                  onChange={() => setMode('Both')}
                />
                <Label htmlFor="both">Both</Label>
              </label>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3">
            <Label htmlFor="nodeId">Target Node</Label>
            <Input
              id="nodeId"
              placeholder="node-id"
              value={nodeId}
              onChange={(e) => setNodeId(e.target.value)}
            />
          </div>
        )}

        {step === 3 && (
          <div className="space-y-3">
            <div>
              <Label htmlFor="fromTime">From</Label>
              <Input id="fromTime" type="datetime-local" value={fromTime}
                onChange={(e) => setFromTime(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="toTime">To</Label>
              <Input id="toTime" type="datetime-local" value={toTime}
                onChange={(e) => setToTime(e.target.value)} />
            </div>
            {rangeError && <p className="text-sm text-destructive">{rangeError}</p>}
          </div>
        )}

        {step === 4 && (
          <div className="space-y-4">
            <div className="rounded-md border p-3 text-sm space-y-1">
              <div><span className="font-medium">Mode:</span> {mode}</div>
              <div><span className="font-medium">Node:</span> {nodeId}</div>
              <div><span className="font-medium">From:</span> {fromTime}</div>
              <div><span className="font-medium">To:</span> {toTime}</div>
            </div>
            {mode === 'FailedDelivery' && (
              <div>
                <Label htmlFor="batchIds">Batch IDs (optional, comma-separated)</Label>
                <Textarea
                  id="batchIds"
                  placeholder="e.g. 1001, 1002, 1003"
                  value={batchIdsText}
                  onChange={(e) => setBatchIdsText(e.target.value)}
                  rows={3}
                />
              </div>
            )}
          </div>
        )}

        <DialogFooter className="gap-2">
          {step > 1 && (
            <Button variant="outline" onClick={() => setStep((s) => Math.max(s - 1, 1) as Step)}>
              Back
            </Button>
          )}
          {step < 4 ? (
            <Button onClick={handleNext} disabled={step === 2 && !nodeId}>
              Next
            </Button>
          ) : (
            <Button onClick={handleSubmit} disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Starting…' : 'Start Replay'}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
