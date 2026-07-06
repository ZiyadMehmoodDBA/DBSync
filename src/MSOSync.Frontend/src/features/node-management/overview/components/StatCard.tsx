interface StatCardProps {
  label:        string;
  value:        number | string;
  description?: string;
}

export function StatCard({ label, value, description }: StatCardProps) {
  return (
    <div className="rounded-lg border p-4 bg-white dark:bg-neutral-900">
      <p className="text-sm text-neutral-500 dark:text-neutral-400">{label}</p>
      <p className="text-2xl font-bold mt-1">{value}</p>
      {description && (
        <p className="text-xs text-neutral-400 mt-1">{description}</p>
      )}
    </div>
  );
}
