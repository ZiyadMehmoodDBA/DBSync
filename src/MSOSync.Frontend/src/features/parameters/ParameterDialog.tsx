import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '../../components/ui/form';
import { Input } from '../../components/ui/input';
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { updateParameterSchema, getDefaultValues } from './schemas';
import type { UpdateParameterForm } from './schemas';
import { useUpdateParameterMutation } from './mutations';
import type { ParameterRow } from './columns';

interface ParameterDialogProps {
  open: boolean;
  initialValues: ParameterRow;
  onOpenChange: (open: boolean) => void;
}

export function ParameterDialog({ open, initialValues, onOpenChange }: ParameterDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const mutation = useUpdateParameterMutation();

  const form = useForm<UpdateParameterForm>({
    resolver: zodResolver(updateParameterSchema),
    defaultValues: getDefaultValues(initialValues),
  });

  useEffect(() => {
    if (open) {
      form.reset(getDefaultValues(initialValues));
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, initialValues, form]);

  const onSubmit = async (values: UpdateParameterForm) => {
    setApiError(null);
    try {
      await mutation.mutateAsync({ name: initialValues.name, value: values.value });
      toast.success('Parameter updated');
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  return (
    <EntityDialog
      open={open}
      title={`Edit Parameter: ${initialValues.name}`}
      description={initialValues.descriptor?.description}
      onOpenChange={onOpenChange}
    >
      <Form {...form}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void form.handleSubmit(onSubmit)(e);
          }}
          className="flex flex-col gap-4"
        >
          <FormField
            control={form.control}
            name="value"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Value</FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    type={initialValues.isSecret ? 'password' : 'text'}
                    autoComplete="off"
                    placeholder={initialValues.isSecret ? 'Enter new secret value' : 'Enter value'}
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel="Update"
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
