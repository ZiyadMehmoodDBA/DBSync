export function FullscreenLoader() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-white dark:bg-neutral-950">
      <div className="flex flex-col items-center gap-4">
        <div className="h-10 w-10 animate-spin rounded-full border-4 border-neutral-300 border-t-neutral-800 dark:border-neutral-600 dark:border-t-neutral-100" />
        <p className="text-sm text-neutral-500 dark:text-neutral-400">Loading…</p>
      </div>
    </div>
  );
}
