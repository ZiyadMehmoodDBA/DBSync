import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

interface Props { onFilter: (filter: AuditFilter) => void; }

export function AuditFilters({ onFilter }: Props) {
  const [username, setUsername] = useState('');
  const [actionName, setActionName] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function handleApply() {
    onFilter({
      username: username || undefined,
      actionName: actionName || undefined,
      from: from || undefined,
      to: to || undefined,
      page: 1,
      pageSize: DEFAULT_PAGE_SIZE,
    });
  }

  function handleReset() {
    setUsername(''); setActionName(''); setFrom(''); setTo('');
    onFilter({ page: 1, pageSize: DEFAULT_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Username</Label>
        <Input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="username" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Action</Label>
        <Input value={actionName} onChange={(e) => setActionName(e.target.value)} placeholder="action name" className="h-8 w-40 text-sm" />
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
