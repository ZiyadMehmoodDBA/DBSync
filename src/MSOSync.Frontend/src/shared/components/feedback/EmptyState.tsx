interface Props {
  message?: string;
}

export function EmptyState({ message = 'No data found' }: Props) {
  return (
    <div className="flex items-center justify-center py-12 text-sm text-neutral-500 dark:text-neutral-400">
      {message}
    </div>
  );
}
