interface Props {
  title: string;
  epic: string;
}

export function PlaceholderPage({ title, epic }: Props) {
  return (
    <div className="flex flex-col gap-2 p-6">
      <h1 className="text-2xl font-semibold">{title}</h1>
      <p className="text-neutral-500 dark:text-neutral-400">
        Coming in Epic {epic}
      </p>
    </div>
  );
}
