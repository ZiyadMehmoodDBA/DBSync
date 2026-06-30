import { ParametersGrid } from './ParametersGrid';

export function ParametersPage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Parameters</h1>
      <p className="text-sm text-neutral-500 dark:text-neutral-400">
        Secret values are masked. Edit actions available in Epic 10C.
      </p>
      <ParametersGrid />
    </div>
  );
}
