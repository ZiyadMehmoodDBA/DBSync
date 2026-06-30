import { Button } from '../../../components/ui/button';

interface FormActionsProps {
  loading: boolean;
  onCancel: () => void;
  submitLabel?: string;
}

export function FormActions({
  loading,
  onCancel,
  submitLabel = 'Save',
}: FormActionsProps) {
  return (
    <div className="flex justify-end gap-2 pt-2">
      <Button type="button" variant="outline" onClick={onCancel} disabled={loading}>
        Cancel
      </Button>
      <Button type="submit" disabled={loading}>
        {loading ? 'Saving…' : submitLabel}
      </Button>
    </div>
  );
}
