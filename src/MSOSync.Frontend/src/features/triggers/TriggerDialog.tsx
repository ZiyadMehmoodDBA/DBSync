import { useEffect, useState, useMemo } from 'react';
import { useForm } from 'react-hook-form';
import type { Resolver } from 'react-hook-form';
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { EntityDialog, FormActions, FormError, FormSection } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { createTriggerSchema, updateTriggerSchema, getDefaultValues } from './schemas';
import type { CreateTriggerForm } from './schemas';
import { useCreateTriggerMutation, useUpdateTriggerMutation } from './mutations';
import { toSourceTable } from './utils';
import { useChannels } from '../channels/hooks';
import type { TriggerDto } from '../../shared/types';

interface TriggerDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: TriggerDto;
  onOpenChange: (open: boolean) => void;
}

export function TriggerDialog({ open, mode, initialValues, onOpenChange }: TriggerDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateTriggerMutation();
  const updateMutation = useUpdateTriggerMutation();
  const { data: channels } = useChannels();

  const schema = mode === 'create' ? createTriggerSchema : updateTriggerSchema;
  const defaultValues = useMemo(
    () => getDefaultValues(initialValues, mode),
    [initialValues, mode],
  );

  const form = useForm<CreateTriggerForm>({
    resolver: zodResolver(schema) as unknown as Resolver<CreateTriggerForm>,
    defaultValues: defaultValues as CreateTriggerForm,
  });

  useEffect(() => {
    if (open) {
      form.reset(defaultValues as CreateTriggerForm);
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, defaultValues, form]);

  const onSubmit = async (values: CreateTriggerForm) => {
    setApiError(null);
    try {
      if (mode === 'create') {
        await createMutation.mutateAsync({
          triggerId: values.triggerId,
          sourceTable: toSourceTable(values.schemaName, values.tableName),
          channelId: values.channelId,
          syncOnInsert: values.syncOnInsert,
          syncOnUpdate: values.syncOnUpdate,
          syncOnDelete: values.syncOnDelete,
        });
        toast.success('Trigger created');
      } else {
        if (!initialValues) return;
        await updateMutation.mutateAsync({
          triggerId: initialValues.triggerId,
          data: {
            sourceTable: toSourceTable(values.schemaName, values.tableName),
            channelId: values.channelId,
            syncOnInsert: values.syncOnInsert,
            syncOnUpdate: values.syncOnUpdate,
            syncOnDelete: values.syncOnDelete,
          },
        });
        toast.success('Trigger updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title =
    mode === 'create'
      ? 'Add Trigger'
      : `Edit Trigger: ${initialValues?.triggerId ?? ''}`;

  return (
    <EntityDialog open={open} title={title} onOpenChange={onOpenChange}>
      <Form {...form}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void form.handleSubmit(onSubmit)(e);
          }}
          className="flex flex-col gap-4"
        >
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="triggerId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Trigger ID</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="e.g. trg_orders" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormSection title="Source Table">
            <FormField
              control={form.control}
              name="schemaName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Schema</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="dbo" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="tableName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Table</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="Orders" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </FormSection>
          <FormField
            control={form.control}
            name="channelId"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Channel</FormLabel>
                <Select onValueChange={field.onChange} value={field.value}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue placeholder="Select channel…" />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {(channels ?? []).map((ch) => (
                      <SelectItem key={ch.channelId} value={ch.channelId}>
                        {ch.channelId}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormSection title="Sync Operations">
            <FormField
              control={form.control}
              name="syncOnInsert"
              render={({ field }) => (
                <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                  <FormControl>
                    <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                  </FormControl>
                  <FormLabel>Sync on Insert</FormLabel>
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="syncOnUpdate"
              render={({ field }) => (
                <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                  <FormControl>
                    <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                  </FormControl>
                  <FormLabel>Sync on Update</FormLabel>
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="syncOnDelete"
              render={({ field }) => (
                <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                  <FormControl>
                    <Checkbox checked={field.value} onCheckedChange={field.onChange} />
                  </FormControl>
                  <FormLabel>Sync on Delete</FormLabel>
                </FormItem>
              )}
            />
            <FormMessage>{form.formState.errors.syncOnInsert?.message}</FormMessage>
          </FormSection>
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel={mode === 'create' ? 'Create Trigger' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
