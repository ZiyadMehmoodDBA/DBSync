import { useState } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

interface Props { onFilter: (filter: OutgoingBatchFilter) => void; }

export function OutgoingBatchFilters({ onFilter }: Props) {
  const [nodeId, setNodeId] = useState('');
  const [channelId, setChannelId] = useState('');
  const [status, setStatus] = useState('');

  function handleApply() {
    onFilter({
      nodeId: nodeId || undefined,
      channelId: channelId || undefined,
      status: status || undefined,
      page: 1,
      pageSize: DEFAULT_BATCH_PAGE_SIZE,
    });
  }

  function handleReset() {
    setNodeId(''); setChannelId(''); setStatus('');
    onFilter({ page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Node</Label>
        <Input value={nodeId} onChange={(e) => setNodeId(e.target.value)} placeholder="node-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Channel</Label>
        <Input value={channelId} onChange={(e) => setChannelId(e.target.value)} placeholder="channel-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Status</Label>
        <select value={status} onChange={(e) => setStatus(e.target.value)} className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm">
          <option value="">All</option>
          <option value="SENT">Sent</option>
          <option value="OK">OK</option>
          <option value="ERROR">Error</option>
          <option value="LOADING">Loading</option>
        </select>
      </div>
      <Button size="sm" onClick={handleApply}>Apply</Button>
      <Button size="sm" variant="outline" onClick={handleReset}>Reset</Button>
    </div>
  );
}
