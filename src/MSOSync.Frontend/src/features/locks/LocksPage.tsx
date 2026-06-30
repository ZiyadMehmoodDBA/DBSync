import { LocksGrid } from './LocksGrid';

export function LocksPage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Locks</h1>
      <p className="text-sm text-neutral-500 dark:text-neutral-400">
        Active distributed locks. Release actions available in Epic 10C.
      </p>
      <LocksGrid />
    </div>
  );
}
