import { zodResolver } from '@hookform/resolvers/zod';
import type { JSX } from 'react';
import { useForm } from 'react-hook-form';

import { TextField } from '../../components/form/fields';
import { convoyFormSchema } from './convoyFormModel';
import type { ConvoyFormValues } from './convoyFormModel';

interface ConvoyFormProps {
  initialValues: ConvoyFormValues;
  submitLabel: string;
  submitting: boolean;
  errorMessage?: string | undefined;
  onSubmit: (values: ConvoyFormValues) => void;
}

export function ConvoyForm({
  initialValues,
  submitLabel,
  submitting,
  errorMessage,
  onSubmit,
}: ConvoyFormProps): JSX.Element {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ConvoyFormValues>({
    resolver: zodResolver(convoyFormSchema),
    defaultValues: initialValues,
  });

  return (
    <form
      noValidate
      onSubmit={(event) => {
        void handleSubmit(onSubmit)(event);
      }}
    >
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}

      <TextField
        label="Departs"
        type="datetime-local"
        error={errors.start?.message}
        {...register('start')}
      />
      <TextField
        label="Expected arrival"
        type="datetime-local"
        error={errors.expectedEnd?.message}
        {...register('expectedEnd')}
      />

      <button type="submit" disabled={submitting}>
        {submitting ? 'Saving…' : submitLabel}
      </button>
    </form>
  );
}
