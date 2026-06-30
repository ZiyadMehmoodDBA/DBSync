import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { NodesGrid } from './NodesGrid';

export function NodesPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Nodes</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search nodes…"
        className="max-w-xs"
      />
      <NodesGrid quickFilterText={search} />
    </div>
  );
}
