import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { RoutersGrid } from './RoutersGrid';

export function RoutersPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Routers</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search routers…" className="max-w-xs" />
      <RoutersGrid quickFilterText={search} />
    </div>
  );
}
