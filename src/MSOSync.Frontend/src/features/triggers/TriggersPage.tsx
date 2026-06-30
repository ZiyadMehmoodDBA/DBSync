import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { TriggersGrid } from './TriggersGrid';

export function TriggersPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Triggers</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search triggers…" className="max-w-xs" />
      <TriggersGrid quickFilterText={search} />
    </div>
  );
}
