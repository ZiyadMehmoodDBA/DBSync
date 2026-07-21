import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import { getAllNodes } from '@/shared/api/nodes';
import { useCreateRollingOperation } from '@/shared/hooks/useRollingOperations';
import type { CreateRollingOperationRequest, RollingKind, WaveAction } from '@/shared/types/rolling';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RollingOperationWizard({ open, onOpenChange }: Props) {
  const [kind, setKind] = useState<RollingKind>('RollingMaintenance');
  const [selectedNodeIds, setSelectedNodeIds] = useState<string[]>([]);
  const [waveSizing, setWaveSizing] = useState<'count' | 'percent'>('count');
  const [waveSize, setWaveSize] = useState<number>(1);
  const [wavePercent, setWavePercent] = useState<number>(25);
  const [gateSoakSeconds, setGateSoakSeconds] = useState<number>(60);
  const [waveAction, setWaveAction] = useState<WaveAction>('manual-confirm');
  const [windowSeconds, setWindowSeconds] = useState<number>(600);
  const [targetVersion, setTargetVersion] = useState<string>('');
  const [verificationTimeoutSeconds, setVerificationTimeoutSeconds] = useState<number>(900);

  const { data: allNodes } = useQuery({
    queryKey: ['nodes'],
    queryFn: ({ signal }) => getAllNodes({ signal }),
    enabled: open,
  });

  const activeNodes = allNodes?.filter(n => n.lifecycleState === 'Active') ?? [];
  const mutation = useCreateRollingOperation();

  const errors: string[] = [];
  if (selectedNodeIds.length === 0) errors.push('At least one node is required.');
  if (waveSizing === 'count' && waveSize < 1) errors.push('Wave size must be ≥ 1.');
  if (waveSizing === 'percent' && (wavePercent < 1 || wavePercent > 100)) errors.push('Wave percent must be 1–100.');
  if (waveAction === 'auto-window' && windowSeconds <= 0) errors.push('Window seconds must be > 0.');
  if (kind === 'RollingUpgrade' && !targetVersion.trim()) errors.push('TargetVersion is required for RollingUpgrade.');
  if (verificationTimeoutSeconds < 30 || verificationTimeoutSeconds > 86400) errors.push('Verification timeout must be 30–86400 seconds.');
  if (gateSoakSeconds < 0 || gateSoakSeconds > 3600) errors.push('Gate soak must be 0–3600 seconds.');

  const isValid = errors.length === 0;

  function toggleNode(nodeId: string) {
    setSelectedNodeIds(prev =>
      prev.includes(nodeId) ? prev.filter(id => id !== nodeId) : [...prev, nodeId],
    );
  }

  function submit() {
    if (!isValid) return;
    const body: CreateRollingOperationRequest = {
      kind,
      nodeIds: selectedNodeIds,
      waveSize:    waveSizing === 'count'   ? waveSize   : undefined,
      wavePercent: waveSizing === 'percent' ? wavePercent : undefined,
      gateSoakSeconds,
      waveAction,
      windowSeconds: waveAction === 'auto-window' ? windowSeconds : undefined,
      targetVersion: kind === 'RollingUpgrade' ? targetVersion.trim() : undefined,
      verificationTimeoutSeconds,
    };
    mutation.mutate(body, { onSuccess: () => { onOpenChange(false); reset(); } });
  }

  function reset() {
    setKind('RollingMaintenance');
    setSelectedNodeIds([]);
    setWaveSizing('count');
    setWaveSize(1);
    setWavePercent(25);
    setGateSoakSeconds(60);
    setWaveAction('manual-confirm');
    setWindowSeconds(600);
    setTargetVersion('');
    setVerificationTimeoutSeconds(900);
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) reset(); onOpenChange(o); }}>
      <DialogContent className="max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>New Rolling Operation</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 text-sm">
          {/* Kind */}
          <div>
            <label className="block font-medium mb-1">Kind</label>
            <Select value={kind} onValueChange={(v) => setKind(v as RollingKind)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="RollingMaintenance">Rolling Maintenance</SelectItem>
                <SelectItem value="RollingUpgrade">Rolling Upgrade</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {/* Node selection */}
          <div>
            <label className="block font-medium mb-1">Active Nodes ({selectedNodeIds.length} selected)</label>
            <div className="border rounded max-h-36 overflow-y-auto p-2 space-y-1">
              {activeNodes.length === 0 && <span className="text-muted-foreground">No active nodes</span>}
              {activeNodes.map(n => (
                <label key={n.nodeId} className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={selectedNodeIds.includes(n.nodeId)}
                    onChange={() => toggleNode(n.nodeId)}
                  />
                  <span>{n.nodeId}</span>
                </label>
              ))}
            </div>
          </div>

          {/* Wave sizing */}
          <div>
            <label className="block font-medium mb-1">Wave sizing</label>
            <div className="flex gap-4">
              <label className="flex items-center gap-1">
                <input type="radio" checked={waveSizing === 'count'} onChange={() => setWaveSizing('count')} />
                By count
              </label>
              <label className="flex items-center gap-1">
                <input type="radio" checked={waveSizing === 'percent'} onChange={() => setWaveSizing('percent')} />
                By percent
              </label>
            </div>
            {waveSizing === 'count' && (
              <input
                type="number" min={1} className="mt-1 w-full rounded border p-2"
                value={waveSize} onChange={e => setWaveSize(Number(e.target.value))}
              />
            )}
            {waveSizing === 'percent' && (
              <input
                type="number" min={1} max={100} className="mt-1 w-full rounded border p-2"
                value={wavePercent} onChange={e => setWavePercent(Number(e.target.value))}
              />
            )}
          </div>

          {/* Gate soak */}
          <div>
            <label className="block font-medium mb-1">Gate soak seconds (0–3600)</label>
            <input
              type="number" min={0} max={3600} className="w-full rounded border p-2"
              value={gateSoakSeconds} onChange={e => setGateSoakSeconds(Number(e.target.value))}
            />
          </div>

          {/* Wave action */}
          <div>
            <label className="block font-medium mb-1">Wave action</label>
            <Select value={waveAction} onValueChange={(v) => setWaveAction(v as WaveAction)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="manual-confirm">Manual confirm</SelectItem>
                <SelectItem value="auto-window">Auto window</SelectItem>
              </SelectContent>
            </Select>
            {waveAction === 'auto-window' && (
              <div className="mt-2">
                <label className="block font-medium mb-1">Window seconds</label>
                <input
                  type="number" min={1} className="w-full rounded border p-2"
                  value={windowSeconds} onChange={e => setWindowSeconds(Number(e.target.value))}
                />
              </div>
            )}
          </div>

          {/* Target version (upgrade only) */}
          {kind === 'RollingUpgrade' && (
            <div>
              <label className="block font-medium mb-1">Target version</label>
              <input
                type="text" className="w-full rounded border p-2"
                value={targetVersion} onChange={e => setTargetVersion(e.target.value)}
                placeholder="e.g. 1.2.3"
              />
            </div>
          )}

          {/* Verification timeout */}
          <div>
            <label className="block font-medium mb-1">Verification timeout seconds (30–86400)</label>
            <input
              type="number" min={30} max={86400} className="w-full rounded border p-2"
              value={verificationTimeoutSeconds} onChange={e => setVerificationTimeoutSeconds(Number(e.target.value))}
            />
          </div>

          {/* Validation errors */}
          {!isValid && (
            <ul className="text-red-600 space-y-1 list-disc pl-4">
              {errors.map(e => <li key={e}>{e}</li>)}
            </ul>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => { reset(); onOpenChange(false); }}>Cancel</Button>
          <Button disabled={!isValid || mutation.isPending} onClick={submit}>
            {mutation.isPending ? 'Creating…' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
