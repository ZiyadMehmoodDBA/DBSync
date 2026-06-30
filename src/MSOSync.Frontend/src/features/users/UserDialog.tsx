import { useEffect, useState, useMemo } from 'react';
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
import { Checkbox } from '../../components/ui/checkbox';
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { createUserSchema, updateUserSchema, getDefaultValues } from './schemas';
import type { CreateUserForm, UpdateUserForm } from './schemas';
import { useCreateUserMutation, useUpdateUserMutation } from './mutations';
import type { UserSummaryDto } from '../../shared/types';

interface UserDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: UserSummaryDto;
  onOpenChange: (open: boolean) => void;
}

export function UserDialog({ open, mode, initialValues, onOpenChange }: UserDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateUserMutation();
  const updateMutation = useUpdateUserMutation();

  const schema = mode === 'create' ? createUserSchema : updateUserSchema;
  const defaultValues = useMemo(
    () => getDefaultValues(initialValues, mode),
    [initialValues, mode],
  );

  const form = useForm<CreateUserForm | UpdateUserForm>({
    resolver: zodResolver(schema),
    defaultValues,
  });

  useEffect(() => {
    if (open) {
      form.reset(defaultValues);
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, defaultValues, form]);

  const onSubmit = async (values: CreateUserForm | UpdateUserForm) => {
    setApiError(null);
    try {
      if (mode === 'create') {
        const v = values as CreateUserForm;
        await createMutation.mutateAsync({ username: v.username, password: v.password, enabled: v.enabled });
        toast.success('User created');
      } else {
        if (!initialValues) return;
        const v = values as UpdateUserForm;
        await updateMutation.mutateAsync({
          userId: initialValues.userId,
          data: { enabled: v.enabled, newPassword: v.newPassword || undefined },
        });
        toast.success('User updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title = mode === 'create' ? 'Add User' : `Edit User: ${initialValues?.username ?? ''}`;

  return (
    <EntityDialog open={open} title={title} onOpenChange={onOpenChange}>
      <Form {...form}>
        <form onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }} className="flex flex-col gap-4">
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="username"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Username</FormLabel>
                  <FormControl>
                    <Input {...field} autoComplete="off" placeholder="e.g. jsmith" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="password"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Password</FormLabel>
                  <FormControl>
                    <Input {...field} type="password" autoComplete="new-password" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          {mode === 'edit' && (
            <FormField
              control={form.control}
              name="newPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>New Password (leave blank to keep current)</FormLabel>
                  <FormControl>
                    <Input {...field} type="password" autoComplete="new-password" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormField
            control={form.control}
            name="enabled"
            render={({ field }) => (
              <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                <FormControl>
                  <Checkbox
                    checked={field.value as boolean}
                    onCheckedChange={field.onChange}
                  />
                </FormControl>
                <FormLabel>Enabled</FormLabel>
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel={mode === 'create' ? 'Create User' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
