import { useState } from 'react';
import type { BatchErrorFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

interface Props { onFilter: (filter: BatchErrorFilter) => void; }

export function BatchErrorFilters({ onFilter }: Props) {
  const [batchId, setBatchId] = useState('');
  const [conflictType, setConflictType] = useState('');
  const [severity, setSeverity] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function handleApply() {
    onFilter({
      batchId: batchId ? Number(batchId) : undefined,
      conflictType: conflictType || undefined,
      severity: severity || undefined,
      from: from || undefined,
      to: to || undefined,
      page: 1,
      pageSize: DEFAULT_BATCH_PAGE_SIZE,
    });
  }

  function handleReset() {
    setBatchId(''); setConflictType(''); setSeverity(''); setFrom(''); setTo('');
    onFilter({ page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Batch ID</Label>
        <Input type="number" value={batchId} onChange={(e) => setBatchId(e.target.value)} placeholder="batch id" className="h-8 w-32 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Conflict Type</Label>
        <Input value={conflictType} onChange={(e) => setConflictType(e.target.value)} placeholder="conflict type" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Severity</Label>
        <select value={severity} onChange={(e) => setSeverity(e.target.value)} className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm">
          <option value="">All</option>
          <option value="CRITICAL">Critical</option>
          <option value="WARNING">Warning</option>
          <option value="INFO">Info</option>
        </select>
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">From</Label>
        <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-8 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">To</Label>
        <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-8 text-sm" />
      </div>
      <Button size="sm" onClick={handleApply}>Apply</Button>
      <Button size="sm" variant="outline" onClick={handleReset}>Reset</Button>
    </div>
  );
}
