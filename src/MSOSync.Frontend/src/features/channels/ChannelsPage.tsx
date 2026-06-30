import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { ChannelsGrid } from './ChannelsGrid';

export function ChannelsPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Channels</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search channels…" className="max-w-xs" />
      <ChannelsGrid quickFilterText={search} />
    </div>
  );
}
