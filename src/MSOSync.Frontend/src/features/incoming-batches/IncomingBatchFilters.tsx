import { useState } from 'react';
import type { IncomingBatchFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

interface Props {
  onFilter: (filter: IncomingBatchFilter) => void;
}

export function IncomingBatchFilters({ onFilter }: Props) {
  const [sourceNodeId, setSourceNodeId] = useState('');
  const [channelId, setChannelId] = useState('');
  const [status, setStatus] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function handleApply() {
    onFilter({
      sourceNodeId: sourceNodeId || undefined,
      channelId: channelId || undefined,
      status: status || undefined,
      from: from || undefined,
      to: to || undefined,
      page: 1,
      pageSize: DEFAULT_BATCH_PAGE_SIZE,
    });
  }

  function handleReset() {
    setSourceNodeId(''); setChannelId(''); setStatus(''); setFrom(''); setTo('');
    onFilter({ page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Source Node</Label>
        <Input value={sourceNodeId} onChange={(e) => setSourceNodeId(e.target.value)} placeholder="node-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Channel</Label>
        <Input value={channelId} onChange={(e) => setChannelId(e.target.value)} placeholder="channel-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Status</Label>
        <select value={status} onChange={(e) => setStatus(e.target.value)} className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm">
          <option value="">All</option>
          <option value="OK">OK</option>
          <option value="LOADING">Loading</option>
          <option value="ERROR">Error</option>
          <option value="CONFLICT">Conflict</option>
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
