import { LocksGrid } from './LocksGrid';

export function LocksPage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Locks</h1>
      <LocksGrid />
    </div>
  );
}
