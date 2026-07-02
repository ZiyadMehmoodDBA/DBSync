import { Button } from '../../../components/ui/button';

interface ActionButtonProps {
  label: string;
  onClick: () => void;
  loading?: boolean;
  disabled?: boolean;
  disabledTitle?: string;
  variant?: 'default' | 'destructive';
}

export function ActionButton({
  label,
  onClick,
  loading = false,
  disabled = false,
  disabledTitle,
  variant = 'default',
}: ActionButtonProps) {
  const isDisabled = loading || disabled;
  const btn = (
    <Button
      variant={variant === 'destructive' ? 'destructive' : 'outline'}
      size="sm"
      onClick={onClick}
      disabled={isDisabled}
      className="h-7 text-xs"
    >
      {loading ? 'Working…' : label}
    </Button>
  );

  if (disabled && disabledTitle) {
    return <span title={disabledTitle}>{btn}</span>;
  }

  return btn;
}
