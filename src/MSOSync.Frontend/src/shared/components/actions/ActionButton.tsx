import { Button } from '../../../components/ui/button';

interface ActionButtonProps {
  label: string;
  onClick: () => void;
  loading?: boolean;
  variant?: 'default' | 'destructive';
}

export function ActionButton({
  label,
  onClick,
  loading = false,
  variant = 'default',
}: ActionButtonProps) {
  return (
    <Button
      variant={variant === 'destructive' ? 'destructive' : 'outline'}
      size="sm"
      onClick={onClick}
      disabled={loading}
      className="h-7 text-xs"
    >
      {loading ? 'Working…' : label}
    </Button>
  );
}
