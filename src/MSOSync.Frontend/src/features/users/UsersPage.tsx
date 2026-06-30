import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { UsersGrid } from './UsersGrid';

export function UsersPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Users</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search users…"
        className="max-w-xs"
      />
      <UsersGrid quickFilterText={search} />
    </div>
  );
}
