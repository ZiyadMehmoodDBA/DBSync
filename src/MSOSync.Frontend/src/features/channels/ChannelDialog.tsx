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
import { channelFormSchema, CHANNEL_FORM_DEFAULTS } from './schemas';
import type { ChannelForm } from './schemas';
import { useCreateChannelMutation, useUpdateChannelMutation } from './mutations';
import type { ChannelDto } from '../../shared/types';

interface ChannelDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: ChannelDto;
  onOpenChange: (open: boolean) => void;
}

export function ChannelDialog({ open, mode, initialValues, onOpenChange }: ChannelDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateChannelMutation();
  const updateMutation = useUpdateChannelMutation();

  const form = useForm<ChannelForm>({
    resolver: zodResolver(channelFormSchema),
    defaultValues: CHANNEL_FORM_DEFAULTS,
  });

  useEffect(() => {
    if (open) {
      const resetVals: ChannelForm =
        mode === 'edit' && initialValues
          ? {
              channelId: initialValues.channelId,
              priority: '0',
              batchSize: '1000',
              maxBatchToSend: '10',
              maxDataSize: '1048576',
            }
          : CHANNEL_FORM_DEFAULTS;
      form.reset(resetVals);
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, mode, initialValues, form]);

  const onSubmit = async (values: ChannelForm) => {
    setApiError(null);
    try {
      const priority = Number(values.priority);
      const batchSize = Number(values.batchSize);
      const maxBatchToSend = Number(values.maxBatchToSend);
      const maxDataSize = Number(values.maxDataSize);

      if (mode === 'create') {
        await createMutation.mutateAsync({
          channelId: values.channelId,
          priority,
          batchSize,
          maxBatchToSend,
          maxDataSize,
        });
        toast.success('Channel created');
      } else {
        if (!initialValues) return;
        await updateMutation.mutateAsync({
          channelId: initialValues.channelId,
          data: { priority, batchSize, maxBatchToSend, maxDataSize },
        });
        toast.success('Channel updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title =
    mode === 'create'
      ? 'Add Channel'
      : `Edit Channel: ${initialValues?.channelId ?? ''}`;

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
              name="channelId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Channel ID</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="e.g. default" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormField
            control={form.control}
            name="priority"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Priority (0–100)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="batchSize"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Batch Size (1–1,000,000)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="maxBatchToSend"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Max Batches to Send (1–10,000)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="maxDataSize"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Max Data Size (bytes)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel={mode === 'create' ? 'Create Channel' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
